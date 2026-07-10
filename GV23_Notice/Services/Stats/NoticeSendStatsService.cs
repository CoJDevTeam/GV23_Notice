using ClosedXML.Excel;
using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models.Workflow.ViewModels;
using GV23_Notice.Services.QA;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;

namespace GV23_Notice.Services.Stats
{
    using ClosedXML.Excel;
    using GV23_Notice.Data;
    using GV23_Notice.Domain.Email;
    using GV23_Notice.Domain.Workflow;
    using GV23_Notice.Domain.Workflow.Entities;
    using GV23_Notice.Models.Workflow.ViewModels;
    using GV23_Notice.Services.Stats;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Options;
    using System.Net.Mail;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    public sealed class ThirdPartyAppealStatsService
            : IThirdPartyAppealStatsService
    {
        private static readonly string[] ReportableStatuses =
        {
            "Printed",
            "Sent",
            "Email-Failed",
            "No-Owner-Email"
        };

        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly INoticeStatsEmailService _statsEmail;
        private readonly ILogger<ThirdPartyAppealStatsService> _logger;

        public ThirdPartyAppealStatsService(
            AppDbContext db,
            IConfiguration config,
            INoticeStatsEmailService statsEmail,
            ILogger<ThirdPartyAppealStatsService> logger)
        {
            _db = db;
            _config = config;
            _statsEmail = statsEmail;
            _logger = logger;
        }

        private string TpaFromAddress =>
            _config["TpaStats:FromAddress"]?.Trim()
            ?? "AngelinaL@joburg.org.za";

        private string TpaFromName =>
            _config["TpaStats:FromName"]?.Trim()
            ?? "Angelina Leboela";

        private string ValuationEnquiriesEmail =>
            _config["TpaStats:ValuationEnquiriesEmail"]?.Trim()
            ?? "ValuationEnquiries@joburg.org.za";

        public async Task<ThirdPartyAppealStatsVm> BuildStatsAsync(
            Guid workflowKey,
            CancellationToken ct)
        {
            var settings = await _db.NoticeSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        x.WorkflowKey == workflowKey ||
                        x.ApprovalKey == workflowKey,
                    ct)
                ?? throw new InvalidOperationException(
                    "Notice settings not found.");

            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.RollId == settings.RollId,
                    ct)
                ?? throw new InvalidOperationException(
                    "Roll not found.");

            var rows = await _db
                .ThirdPartyAppealApplicationNotices
                .AsNoTracking()
                .Where(x =>
                    (
                        x.NoticeSettingsId == settings.Id ||
                        (
                            x.RollId == settings.RollId &&
                            x.ValuationPeriod ==
                                settings.ValuationPeriodCode
                        )
                    ) &&
                    ReportableStatuses.Contains(x.Status))
                .Select(x => new
                {
                    x.Id,
                    x.AdminName,
                    x.AdminEmail,
                    x.Premise_ID,
                    x.Objection_No,
                    x.Appeal_No,
                    x.Property_Description,
                    x.Status,
                    x.SentAt,
                    x.SentBy,
                    x.VabBoardId,
                    x.HearingDate
                })
                .OrderBy(x => x.AdminName)
                .ThenBy(x => x.Appeal_No)
                .ToListAsync(ct);

            var boardIds = rows
                .Where(x => x.VabBoardId.HasValue)
                .Select(x => x.VabBoardId!.Value)
                .Distinct()
                .ToList();

            var boards = boardIds.Count == 0
                ? new Dictionary<int, VabBoard>()
                : await _db.VabBoards
                    .AsNoTracking()
                    .Include(x => x.Members)
                    .Where(x => boardIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, ct);

            var valuationEnquiriesEmail = ValuationEnquiriesEmail;

            var adminGroups = rows
                .GroupBy(x =>
                    NormalizeAdminKey(
                        x.AdminEmail,
                        x.AdminName))
                .Select(group =>
                {
                    var first = group.First();

                    var ccEmails = new[]
                        {
                        valuationEnquiriesEmail
                        }
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(x))
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    return new ThirdPartyAppealAdminStatsVm
                    {
                        AdminKey = group.Key,

                        AdminName =
                            string.IsNullOrWhiteSpace(
                                first.AdminName)
                                ? "Unassigned Admin"
                                : first.AdminName.Trim(),

                        AdminEmail =
                            first.AdminEmail?.Trim() ?? "",

                        DefaultCcEmails =
                            string.Join(
                                "; ",
                                ccEmails),

                        TotalRecords =
                            group.Count(),

                        TotalPrinted =
                            group.Count(x =>
                                IsStatus(
                                    x.Status,
                                    "Printed")),

                        TotalSent =
                            group.Count(x =>
                                IsStatus(
                                    x.Status,
                                    "Sent")),

                        TotalFailed =
                            group.Count(x =>
                                IsStatus(
                                    x.Status,
                                    "Email-Failed")),

                        TotalNoEmail =
                            group.Count(x =>
                                IsStatus(
                                    x.Status,
                                    "No-Owner-Email")),

                        LastSentAt = group
                            .Where(x => x.SentAt.HasValue)
                            .OrderByDescending(
                                x => x.SentAt)
                            .Select(x => x.SentAt)
                            .FirstOrDefault(),

                        LastSentBy = group
                            .Where(x => x.SentAt.HasValue)
                            .OrderByDescending(
                                x => x.SentAt)
                            .Select(x => x.SentBy)
                            .FirstOrDefault(),

                        Details = group
                            .Select(x =>
                                new ThirdPartyAppealStatsDetailVm
                                {
                                    PremiseId =
                                        x.Premise_ID ?? "",

                                    ObjectionNo =
                                        x.Objection_No ?? "",

                                    AppealNo =
                                        x.Appeal_No ?? "",

                                    PropertyDescription =
                                        x.Property_Description ?? "",

                                    Status =
                                        MapDisplayStatus(
                                            x.Status),

                                    DateSent =
                                        x.SentAt,

                                    SentBy =
                                        x.SentBy ?? "",

                                    VabBoardId =
                                        x.VabBoardId,

                                    HearingDate =
                                        x.HearingDate
                                })
                            .ToList(),

                        BoardGroups = group
                            .GroupBy(x => new
                            {
                                x.VabBoardId,
                                HearingDate = x.HearingDate?.Date
                            })
                            .Select(boardGroup =>
                            {
                                VabBoard? board = null;

                                if (boardGroup.Key.VabBoardId.HasValue)
                                {
                                    boards.TryGetValue(
                                        boardGroup.Key.VabBoardId.Value,
                                        out board);
                                }

                                return new ThirdPartyAppealBoardGroupVm
                                {
                                    VabBoardId =
                                        boardGroup.Key.VabBoardId,

                                    BoardCode =
                                        board?.BoardCode
                                        ?? "Board not assigned",

                                    BoardName =
                                        board?.BoardName
                                        ?? "Board not assigned",

                                    HearingDate =
                                        boardGroup.Key.HearingDate,

                                    Members = board?.Members
                                        .Where(member => member.IsActive)
                                        .OrderBy(member => member.DisplayOrder)
                                        .Select(member =>
                                            new ThirdPartyAppealBoardMemberVm
                                            {
                                                MemberRole =
                                                    member.MemberRole,

                                                NameAndSurname =
                                                    member.NameAndSurname,

                                                CojValuerTeam =
                                                    member.CojValuerTeam ?? "",

                                                CojEmail =
                                                    member.CojEmail ?? "",

                                                EmailAddress =
                                                    member.EmailAddress ?? "",

                                                DisplayOrder =
                                                    member.DisplayOrder
                                            })
                                        .ToList()
                                        ?? new List<ThirdPartyAppealBoardMemberVm>()
                                };
                            })
                            .OrderBy(x => x.HearingDate)
                            .ThenBy(x => x.BoardCode)
                            .ToList()
                    };
                })
                .OrderBy(x => x.AdminName)
                .ToList();

            return new ThirdPartyAppealStatsVm
            {
                WorkflowKey = workflowKey,

                SettingsId = settings.Id,

                RollId = settings.RollId,

                Version = settings.Version,

                RollShortCode =
                    roll.ShortCode ?? "",

                RollName =
                    roll.Name ?? "",

                ValuationPeriod =
                    settings.ValuationPeriodCode ?? "",

                VersionText =
                    $"V{settings.Version}",

                TotalAdmins =
                    adminGroups.Count,

                TotalRecords =
                    rows.Count,

                TotalPrinted =
                    rows.Count(x =>
                        IsStatus(
                            x.Status,
                            "Printed")),

                TotalSent =
                    rows.Count(x =>
                        IsStatus(
                            x.Status,
                            "Sent")),

                TotalFailed =
                    rows.Count(x =>
                        IsStatus(
                            x.Status,
                            "Email-Failed")),

                TotalNoEmail =
                    rows.Count(x =>
                        IsStatus(
                            x.Status,
                            "No-Owner-Email")),

                TotalMissingAdminEmail =
                    adminGroups.Count(x =>
                        !x.HasAdminEmail),

                LastSentAt = rows
                    .Where(x => x.SentAt.HasValue)
                    .OrderByDescending(x => x.SentAt)
                    .Select(x => x.SentAt)
                    .FirstOrDefault(),

                LastSentBy = rows
                    .Where(x => x.SentAt.HasValue)
                    .OrderByDescending(x => x.SentAt)
                    .Select(x => x.SentBy)
                    .FirstOrDefault(),

                ValuationEnquiriesEmail =
                    valuationEnquiriesEmail,

                Admins =
                    adminGroups
            };
        }

        public async Task<ThirdPartyAppealAdminExcelVm> BuildAdminExcelAsync(
            Guid workflowKey,
            string adminKey,
            CancellationToken ct)
        {
            if (workflowKey == Guid.Empty)
            {
                throw new ArgumentException(
                    "A valid workflow key is required.",
                    nameof(workflowKey));
            }

            if (string.IsNullOrWhiteSpace(adminKey))
            {
                throw new ArgumentException(
                    "An Admin key is required.",
                    nameof(adminKey));
            }

            var stats = await BuildStatsAsync(workflowKey, ct);

            var admin = stats.Admins.FirstOrDefault(x =>
                string.Equals(
                    x.AdminKey,
                    adminKey.Trim(),
                    StringComparison.OrdinalIgnoreCase));

            if (admin == null)
            {
                throw new InvalidOperationException(
                    "The selected Admin statistics were not found.");
            }

            if (!admin.HasRecordsToReport)
            {
                throw new InvalidOperationException(
                    "The selected Admin has no TPA records to report.");
            }

            using var workbook = new XLWorkbook();

            BuildSummarySheet(workbook, stats, admin);
            BuildDetailSheet(workbook, admin);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            var safeAdminName = MakeSafeFileName(
                string.IsNullOrWhiteSpace(admin.AdminName)
                    ? "Unassigned_Admin"
                    : admin.AdminName);

            var fileName =
                $"TPA_Notice_Stats_{safeAdminName}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

            return new ThirdPartyAppealAdminExcelVm
            {
                FileName = fileName,
                Content = stream.ToArray()
            };
        }


        private async Task<ThirdPartyAppealStatsSendResultVm>
    SendAdminReportInternalAsync(
        Guid workflowKey,
        string adminKey,
        string performedBy,
        CancellationToken ct)
        {
            var stats = await BuildStatsAsync(
                workflowKey,
                ct);

            var admin = stats.Admins.FirstOrDefault(x =>
                string.Equals(
                    x.AdminKey,
                    adminKey?.Trim(),
                    StringComparison.OrdinalIgnoreCase));

            if (admin == null)
            {
                throw new InvalidOperationException(
                    "The selected Admin statistics were not found.");
            }

            if (!admin.HasAdminEmail)
            {
                await WriteAuditAsync(
                    stats,
                    WorkflowAuditAction.TpaStatsEmailBlocked,
                    performedBy,
                    admin,
                    null,
                    null,
                    "Admin email is missing.",
                    ct);

                throw new InvalidOperationException(
                    $"The report for {admin.AdminName} cannot be sent because the Admin email is missing.");
            }

            if (!admin.HasRecordsToReport)
            {
                throw new InvalidOperationException(
                    $"The report for {admin.AdminName} has no records.");
            }

            var result =
                new ThirdPartyAppealStatsSendResultVm
                {
                    AdminName = admin.AdminName,
                    AdminEmail = admin.AdminEmail,
                    CcEmails = ValuationEnquiriesEmail
                };

            try
            {
                var excel = await BuildAdminExcelAsync(
                    workflowKey,
                    adminKey,
                    ct);

                var evidenceFolder =
                    BuildEvidenceFolder(
                        stats,
                        admin);

                Directory.CreateDirectory(
                    evidenceFolder);

                result.ExcelPath = Path.Combine(
                    evidenceFolder,
                    excel.FileName);

                await File.WriteAllBytesAsync(
                    result.ExcelPath,
                    excel.Content,
                    ct);

                var subject =
                    BuildEmailSubject(stats);

                var htmlBody =
                    BuildEmailBody(
                        stats,
                        admin);

                result.EmlPath = Path.Combine(
                    evidenceFolder,
                    Path.ChangeExtension(
                        excel.FileName,
                        ".eml"));

                SaveEmailEvidence(
                    toEmails:
                        admin.AdminEmail,

                    ccEmails:
                        ValuationEnquiriesEmail,

                    subject:
                        subject,

                    htmlBody:
                        htmlBody,

                    attachmentPath:
                        result.ExcelPath,

                    emlPath:
                        result.EmlPath);

                await _statsEmail.SendStatsEmailAsync(
                    toEmails:
                        admin.AdminEmail,

                    ccEmails:
                        ValuationEnquiriesEmail,

                    subject:
                        subject,

                    htmlBody:
                        htmlBody,

                    attachmentPath:
                        result.ExcelPath,

                    ct:
                        ct);

                result.Success = true;

                await WriteAuditAsync(
                    stats,
                    WorkflowAuditAction.TpaStatsEmailSent,
                    performedBy,
                    admin,
                    result.ExcelPath,
                    result.EmlPath,
                    null,
                    ct);

                _logger.LogInformation(
                    "TPA Admin report sent to {AdminEmail}. Excel: {ExcelPath}. EML: {EmlPath}",
                    admin.AdminEmail,
                    result.ExcelPath,
                    result.EmlPath);

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;

                try
                {
                    await WriteAuditAsync(
                        stats,
                        WorkflowAuditAction.TpaStatsEmailFailed,
                        performedBy,
                        admin,
                        result.ExcelPath,
                        result.EmlPath,
                        ex.Message,
                        ct);
                }
                catch (Exception auditEx)
                {
                    _logger.LogError(
                        auditEx,
                        "The TPA report failure audit could not be saved for {AdminEmail}.",
                        admin.AdminEmail);
                }

                _logger.LogError(
                    ex,
                    "Failed to create, archive or send the TPA Admin report to {AdminEmail}.",
                    admin.AdminEmail);

                return result;
            }
        }
        public async Task<ThirdPartyAppealStatsSendResultVm> SendAdminReportAsync(
            Guid workflowKey,
            string adminKey,
            string performedBy,
            CancellationToken ct)
        {
            var stats = await BuildStatsAsync(workflowKey, ct);

            var admin = stats.Admins.FirstOrDefault(x =>
                string.Equals(
                    x.AdminKey,
                    adminKey?.Trim(),
                    StringComparison.OrdinalIgnoreCase));

            if (admin == null)
            {
                throw new InvalidOperationException(
                    "The selected Admin statistics were not found.");
            }

            if (!admin.HasAdminEmail)
            {
                await WriteAuditAsync(
                    stats,
                    WorkflowAuditAction.TpaStatsEmailBlocked,
                    performedBy,
                    admin,
                    null,
                    null,
                    "Admin email is missing.",
                    ct);

                throw new InvalidOperationException(
                    $"The report for {admin.AdminName} cannot be sent because the Admin email is missing.");
            }

            if (!admin.HasRecordsToReport)
            {
                throw new InvalidOperationException(
                    $"The report for {admin.AdminName} has no records.");
            }

            var result = new ThirdPartyAppealStatsSendResultVm
            {
                AdminName = admin.AdminName,
                AdminEmail = admin.AdminEmail,
                CcEmails = ValuationEnquiriesEmail
            };

            try
            {
                var excel = await BuildAdminExcelAsync(
                    workflowKey,
                    adminKey,
                    ct);

                var evidenceFolder = BuildEvidenceFolder(
                    stats,
                    admin);

                Directory.CreateDirectory(evidenceFolder);

                result.ExcelPath = Path.Combine(
                    evidenceFolder,
                    excel.FileName);

                await File.WriteAllBytesAsync(
                    result.ExcelPath,
                    excel.Content,
                    ct);

                var subject = BuildEmailSubject(stats);
                var htmlBody = BuildEmailBody(stats, admin);

                result.EmlPath = Path.Combine(
                    evidenceFolder,
                    Path.ChangeExtension(excel.FileName, ".eml"));

                SaveEmailEvidence(
                    toEmails: admin.AdminEmail,
                    ccEmails: ValuationEnquiriesEmail,
                    subject: subject,
                    htmlBody: htmlBody,
                    attachmentPath: result.ExcelPath,
                    emlPath: result.EmlPath);

                await _statsEmail.SendStatsEmailAsync(
                    toEmails: admin.AdminEmail,
                    ccEmails: ValuationEnquiriesEmail,
                    subject: subject,
                    htmlBody: htmlBody,
                    attachmentPath: result.ExcelPath,
                    ct: ct);
                   /// fromAddress: TpaFromAddress,
                   // fromName: TpaFromName);

                result.Success = true;

                await WriteAuditAsync(
                    stats,
                    WorkflowAuditAction.TpaStatsEmailSent,
                    performedBy,
                    admin,
                    result.ExcelPath,
                    result.EmlPath,
                    null,
                    ct);

                _logger.LogInformation(
                    "TPA Admin stats report sent to {AdminEmail}. Excel: {ExcelPath}. EML: {EmlPath}",
                    admin.AdminEmail,
                    result.ExcelPath,
                    result.EmlPath);

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;

                try
                {
                    await WriteAuditAsync(
                        stats,
                        WorkflowAuditAction.TpaStatsEmailFailed,
                        performedBy,
                        admin,
                        result.ExcelPath,
                        result.EmlPath,
                        ex.Message,
                        ct);
                }
                catch (Exception auditEx)
                {
                    _logger.LogError(
                        auditEx,
                        "The TPA stats failure audit could not be saved for {AdminEmail}.",
                        admin.AdminEmail);
                }

                _logger.LogError(
                    ex,
                    "Failed to create, archive or send the TPA Admin stats report to {AdminEmail}.",
                    admin.AdminEmail);

                return result;
            }
        }

        public async Task<ThirdPartyAppealStatsBulkSendResultVm>
      SendAllAdminReportsAsync(
          Guid workflowKey,
          string performedBy,
          CancellationToken ct)
        {
            var stats =
                await BuildStatsAsync(
                    workflowKey,
                    ct);

            var bulk =
                new ThirdPartyAppealStatsBulkSendResultVm
                {
                    TotalAdmins =
                        stats.Admins.Count
                };

            foreach (var admin in stats.Admins)
            {
                ct.ThrowIfCancellationRequested();

                if (!admin.HasAdminEmail)
                {
                    bulk.Skipped++;

                    var skipped =
                        new ThirdPartyAppealStatsSendResultVm
                        {
                            Success = false,
                            AdminName = admin.AdminName,
                            AdminEmail = admin.AdminEmail,
                            CcEmails =
                                ValuationEnquiriesEmail,

                            ErrorMessage =
                                "Skipped because the Admin email is missing."
                        };

                    bulk.Results.Add(skipped);

                    await WriteAuditAsync(
                        stats,
                        WorkflowAuditAction
                            .TpaStatsEmailBlocked,
                        performedBy,
                        admin,
                        null,
                        null,
                        skipped.ErrorMessage,
                        ct);

                    continue;
                }

                if (!admin.HasRecordsToReport)
                {
                    bulk.Skipped++;

                    bulk.Results.Add(
                        new ThirdPartyAppealStatsSendResultVm
                        {
                            Success = false,
                            AdminName = admin.AdminName,
                            AdminEmail = admin.AdminEmail,
                            CcEmails =
                                ValuationEnquiriesEmail,

                            ErrorMessage =
                                "Skipped because the Admin has no records to report."
                        });

                    continue;
                }

                var result =
                    await SendAdminReportInternalAsync(
                        workflowKey,
                        admin.AdminKey,
                        performedBy,
                        ct);

                bulk.Results.Add(result);

                if (result.Success)
                {
                    bulk.Sent++;
                }
                else
                {
                    bulk.Failed++;
                }
            }

            return bulk;
        }

        private string BuildEvidenceFolder(
            ThirdPartyAppealStatsVm stats,
            ThirdPartyAppealAdminStatsVm admin)
        {
            var configuredRoot =
                _config["TpaStats:EvidenceRoot"]?.Trim();

            var root = string.IsNullOrWhiteSpace(configuredRoot)
                ? Path.Combine(
                    AppContext.BaseDirectory,
                    "App_Data",
                    "TPA Stats Evidence")
                : configuredRoot;

            return Path.Combine(
                root,
                MakeSafeFileName(stats.RollShortCode),
                MakeSafeFileName(stats.ValuationPeriod),
                DateTime.Now.ToString("yyyyMMdd"),
                MakeSafeFileName(admin.AdminName));
        }

        private static string BuildEmailSubject(
            ThirdPartyAppealStatsVm stats)
        {
            return
                $"Third-Party Appeal Notice Distribution Report – {stats.ValuationPeriod}";
        }

        private static string BuildEmailBody(
            ThirdPartyAppealStatsVm stats,
            ThirdPartyAppealAdminStatsVm admin)
        {
            var adminName = EncodeHtml(
                string.IsNullOrWhiteSpace(admin.AdminName)
                    ? "Admin"
                    : admin.AdminName);

            var boardSections = admin.BoardGroups.Count == 0
                ? """
              <p style="color:#8a1a1a;font-weight:bold;">
                  No VAB board members have been linked to these records.
              </p>
              """
                : string.Join(
                    Environment.NewLine,
                    admin.BoardGroups.Select(BuildBoardGroupHtml));

            return $"""
<!doctype html>
<html>
<body style="font-family:Arial,sans-serif;color:#222;line-height:1.6;font-size:14px;">
    <p>Dear {adminName},</p>

    <p>
        Please schedule the Appeal Board hearings on the AKON system and send
        the corresponding Teams invitations as per the information provided below.
    </p>

    <p>
        Copy the <strong>A_Premise ID</strong> from
        <strong>Sheet 2, Column A</strong> of the attached file and paste it into
        a new Excel spreadsheet for upload to the AKON system.
    </p>

    <p>
        The file for the public is attached. Please ensure that this file is
        included as an attachment in the Teams invitation.
    </p>

    <p>
        The following Appeal Board members should be invited, including
        COJ Legal Team Valuers:
    </p>

    {boardSections}

    <p>Regards,</p>
</body>
</html>
""";
        }

        private static string BuildBoardGroupHtml(
            ThirdPartyAppealBoardGroupVm boardGroup)
        {
            var boardTitle = EncodeHtml(
                string.IsNullOrWhiteSpace(boardGroup.BoardName)
                    ? boardGroup.BoardCode
                    : boardGroup.BoardName);

            var hearingDate = boardGroup.HearingDate.HasValue
                ? boardGroup.HearingDate.Value.ToString("d MMMM yyyy")
                : "Not assigned";

            var memberRows = boardGroup.Members.Count == 0
                ? """
              <tr>
                  <td colspan="6"
                      style="border:1px solid #cfcfcf;padding:8px;color:#8a1a1a;">
                      No active board members are configured for this board.
                  </td>
              </tr>
              """
                : string.Join(
                    Environment.NewLine,
                    boardGroup.Members
                        .OrderBy(x => x.DisplayOrder)
                        .Select(member => $"""
                        <tr>
                            <td style="border:1px solid #cfcfcf;padding:8px;">
                                {EncodeHtml(member.MemberRole)}
                            </td>
                            <td style="border:1px solid #cfcfcf;padding:8px;">
                                {EncodeHtml(member.NameAndSurname)}
                            </td>
                            <td style="border:1px solid #cfcfcf;padding:8px;">
                                {EncodeHtml(member.CojValuerTeam)}
                            </td>
                            <td style="border:1px solid #cfcfcf;padding:8px;">
                                {EncodeHtml(hearingDate)}
                            </td>
                            <td style="border:1px solid #cfcfcf;padding:8px;">
                                {EncodeHtml(member.CojEmail)}
                            </td>
                            <td style="border:1px solid #cfcfcf;padding:8px;">
                                {EncodeHtml(member.EmailAddress)}
                            </td>
                        </tr>
                        """));

            return $"""
<section style="margin:22px 0;">
    <div style="
        background:#111;
        color:#fff;
        padding:10px 12px;
        font-weight:bold;">
        {boardTitle}
    </div>

    <table cellpadding="0"
           cellspacing="0"
           style="
               width:100%;
               border-collapse:collapse;
               margin-top:0;
               font-family:Arial,sans-serif;
               font-size:13px;">
        <thead>
            <tr style="background:#e6b000;color:#111;">
                <th style="border:1px solid #cfcfcf;padding:8px;text-align:left;">
                    Role
                </th>
                <th style="border:1px solid #cfcfcf;padding:8px;text-align:left;">
                    Name and Surname
                </th>
                <th style="border:1px solid #cfcfcf;padding:8px;text-align:left;">
                    COJ Valuers
                </th>
                <th style="border:1px solid #cfcfcf;padding:8px;text-align:left;">
                    Hearing Date
                </th>
                <th style="border:1px solid #cfcfcf;padding:8px;text-align:left;">
                    COJ Email
                </th>
                <th style="border:1px solid #cfcfcf;padding:8px;text-align:left;">
                    Email Address
                </th>
            </tr>
        </thead>
        <tbody>
            {memberRows}
        </tbody>
    </table>
</section>
""";
        }

        private static string EncodeHtml(string? value)
        {
            return System.Net.WebUtility.HtmlEncode(
                string.IsNullOrWhiteSpace(value)
                    ? "—"
                    : value.Trim());
        }

        private void SaveEmailEvidence(
            string toEmails,
            string? ccEmails,
            string subject,
            string htmlBody,
            string attachmentPath,
            string emlPath)
        {
            var pickupFolder = Path.Combine(
                Path.GetDirectoryName(emlPath)!,
                "_eml_temp");

            Directory.CreateDirectory(pickupFolder);

            using var message = CreateMailMessage(
                toEmails,
                ccEmails,
                subject,
                htmlBody,
                attachmentPath);

            using var pickupClient = new SmtpClient
            {
                DeliveryMethod =
                    SmtpDeliveryMethod.SpecifiedPickupDirectory,
                PickupDirectoryLocation = pickupFolder
            };

            pickupClient.Send(message);

            var generated = new DirectoryInfo(pickupFolder)
                .GetFiles("*.eml")
                .OrderByDescending(x => x.LastWriteTimeUtc)
                .FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "The TPA stats email evidence file could not be created.");

            if (File.Exists(emlPath))
            {
                File.Delete(emlPath);
            }

            generated.MoveTo(emlPath);

            if (Directory.Exists(pickupFolder) &&
                !Directory.EnumerateFileSystemEntries(pickupFolder).Any())
            {
                Directory.Delete(pickupFolder);
            }
        }

        private MailMessage CreateMailMessage(
            string toEmails,
            string? ccEmails,
            string subject,
            string htmlBody,
            string attachmentPath)
        {
            var fromAddress = TpaFromAddress;
            var fromName = TpaFromName;

            var message = new MailMessage
            {
                From = new MailAddress(fromAddress, fromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };

            AddAddresses(message.To, toEmails);
            AddAddresses(message.CC, ccEmails);

            message.Attachments.Add(
                new Attachment(
                    attachmentPath,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));

            return message;
        }

        private static void AddAddresses(
            MailAddressCollection collection,
            string? addresses)
        {
            if (string.IsNullOrWhiteSpace(addresses))
            {
                return;
            }

            foreach (var address in addresses.Split(
                new[] { ';', ',' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                var clean = address.Trim();

                if (!string.IsNullOrWhiteSpace(clean))
                {
                    collection.Add(new MailAddress(clean));
                }
            }
        }

        private async Task WriteAuditAsync(
            ThirdPartyAppealStatsVm stats,
            WorkflowAuditAction action,
            string performedBy,
            ThirdPartyAppealAdminStatsVm admin,
            string? excelPath,
            string? emlPath,
            string? errorMessage,
            CancellationToken ct)
        {
            var meta = new
            {
                admin.AdminKey,
                admin.AdminName,
                admin.AdminEmail,
                CcEmails = ValuationEnquiriesEmail,
                admin.TotalRecords,
                admin.TotalSent,
                admin.TotalFailed,
                admin.TotalNoEmail,
                admin.TotalPrinted,
                ExcelPath = excelPath,
                EmlPath = emlPath,
                ErrorMessage = errorMessage
            };

            _db.NoticeWorkflowAuditLogs.Add(
                new NoticeWorkflowAuditLog
                {
                    NoticeSettingsId = stats.SettingsId,
                    RollId = stats.RollId,
                    Notice = NoticeKind.TPA,
                    Version = stats.Version,
                    Action = action,
                    PerformedBy = string.IsNullOrWhiteSpace(performedBy)
                        ? "Unknown"
                        : performedBy,
                    PerformedAtUtc = DateTime.UtcNow,
                    Notes = BuildAuditNotes(
                        action,
                        admin,
                        errorMessage),
                    MetaJson = JsonSerializer.Serialize(meta)
                });

            await _db.SaveChangesAsync(ct);
        }

        private static string BuildAuditNotes(
            WorkflowAuditAction action,
            ThirdPartyAppealAdminStatsVm admin,
            string? errorMessage)
        {
            return action switch
            {
                WorkflowAuditAction.TpaStatsEmailSent =>
                    $"TPA stats report sent to {admin.AdminName} ({admin.AdminEmail}).",

                WorkflowAuditAction.TpaStatsEmailFailed =>
                    $"TPA stats report failed for {admin.AdminName} ({admin.AdminEmail}). Error: {errorMessage}",

                WorkflowAuditAction.TpaStatsEmailBlocked =>
                    $"TPA stats report blocked for {admin.AdminName}. {errorMessage}",

                _ =>
                    $"TPA stats report action for {admin.AdminName}."
            };
        }

        private static void BuildSummarySheet(
            XLWorkbook workbook,
            ThirdPartyAppealStatsVm stats,
            ThirdPartyAppealAdminStatsVm admin)
        {
            var sheet = workbook.Worksheets.Add("Summary Report");

            sheet.Cell("A1").Value =
                "THIRD-PARTY APPEAL NOTICE DISTRIBUTION REPORT";
            sheet.Range("A1:D1").Merge();
            sheet.Range("A1:D1").Style
                .Font.SetBold()
                .Font.SetFontSize(16)
                .Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(XLColor.FromHtml("#111111"));
            sheet.Range("A1:D1").Style.Alignment
                .SetHorizontal(XLAlignmentHorizontalValues.Center);
            sheet.Row(1).Height = 28;

            sheet.Cell("A3").Value = "Admin Name";
            sheet.Cell("B3").Value = admin.AdminName;
            sheet.Cell("A4").Value = "Admin Email";
            sheet.Cell("B4").Value = admin.AdminEmail;

            sheet.Cell("A6").Value = "Roll";
            sheet.Cell("B6").Value =
                $"{stats.RollShortCode} — {stats.RollName}";
            sheet.Cell("A7").Value = "Valuation Period";
            sheet.Cell("B7").Value = stats.ValuationPeriod;
            sheet.Cell("A8").Value = "Version";
            sheet.Cell("B8").Value = stats.VersionText;
            sheet.Cell("A9").Value = "Report Generated";
            sheet.Cell("B9").Value = DateTime.Now;
            sheet.Cell("B9").Style.DateFormat.Format =
                "dd MMM yyyy HH:mm";

            sheet.Cell("A11").Value = "Summary";
            sheet.Range("A11:B11").Merge();
            StyleSectionHeader(sheet.Range("A11:B11"));

            sheet.Cell("A12").Value = "Total TPA Notices";
            sheet.Cell("B12").Value = admin.TotalRecords;
            sheet.Cell("A13").Value = "Notice-Sent";
            sheet.Cell("B13").Value = admin.TotalSent;
            sheet.Cell("A14").Value = "Printed / Awaiting Send";
            sheet.Cell("B14").Value = admin.TotalPrinted;
            sheet.Cell("A15").Value = "Email-Failed";
            sheet.Cell("B15").Value = admin.TotalFailed;
            sheet.Cell("A16").Value = "No-Owner-Email";
            sheet.Cell("B16").Value = admin.TotalNoEmail;

            sheet.Cell("A18").Value = "Status";
            sheet.Cell("B18").Value = "Count";
            StyleTableHeader(sheet.Range("A18:B18"));

            sheet.Cell("A19").Value = "Notice-Sent";
            sheet.Cell("B19").Value = admin.TotalSent;
            sheet.Cell("A20").Value = "Printed / Awaiting Send";
            sheet.Cell("B20").Value = admin.TotalPrinted;
            sheet.Cell("A21").Value = "Email-Failed";
            sheet.Cell("B21").Value = admin.TotalFailed;
            sheet.Cell("A22").Value = "No-Owner-Email";
            sheet.Cell("B22").Value = admin.TotalNoEmail;

            sheet.Range("A3:A9").Style.Font.SetBold();
            sheet.Range("A12:A16").Style.Font.SetBold();
            sheet.Range("B12:B16").Style
                .Font.SetBold()
                .Font.SetFontSize(12);

            sheet.Range("A3:B9").Style.Border
                .SetBottomBorder(XLBorderStyleValues.Thin)
                .Border.SetBottomBorderColor(XLColor.FromHtml("#E6E6E6"));

            sheet.Range("A12:B16").Style.Border
                .SetBottomBorder(XLBorderStyleValues.Thin)
                .Border.SetBottomBorderColor(XLColor.FromHtml("#E6E6E6"));

            sheet.Range("A18:B22").Style.Border
                .SetOutsideBorder(XLBorderStyleValues.Thin)
                .Border.SetInsideBorder(XLBorderStyleValues.Thin)
                .Border.SetOutsideBorderColor(XLColor.FromHtml("#D9D9D9"))
                .Border.SetInsideBorderColor(XLColor.FromHtml("#D9D9D9"));

            sheet.Cell("A24").Value =
                "The Detail sheet contains A_Premise_ID, Objection Number, Appeal Number, Property Description, Status, Date Sent and Sent By.";
            sheet.Range("A24:D24").Merge();
            sheet.Range("A24:D24").Style
                .Font.SetItalic()
                .Font.SetFontColor(XLColor.FromHtml("#666666"));
            sheet.Range("A24:D24").Style.Alignment.SetWrapText();

            sheet.Column("A").Width = 28;
            sheet.Column("B").Width = 48;
            sheet.Column("C").Width = 18;
            sheet.Column("D").Width = 18;
            sheet.SheetView.FreezeRows(1);
            sheet.PageSetup.PageOrientation =
                XLPageOrientation.Portrait;
            sheet.PageSetup.FitToPages(1, 1);
        }

        private static void BuildDetailSheet(
            XLWorkbook workbook,
            ThirdPartyAppealAdminStatsVm admin)
        {
            var sheet = workbook.Worksheets.Add("Detail");

            var headers = new[]
            {
            "A_Premise_ID",
            "Objection_No",
            "Appeal_No",
            "Property_Desc",
            "Status",
            "Date Sent",
            "Sent By Who"
        };

            for (var column = 0;
                 column < headers.Length;
                 column++)
            {
                sheet.Cell(1, column + 1).Value =
                    headers[column];
            }

            StyleTableHeader(
                sheet.Range(1, 1, 1, headers.Length));

            /*
             * Row 2 is intentionally left empty.
             * The first detail record starts on row 3.
             */
            var rowNumber = 3;

            foreach (var row in admin.Details
                .OrderBy(x => x.AppealNo)
                .ThenBy(x => x.PremiseId))
            {
                sheet.Cell(rowNumber, 1).Value =
                    row.PremiseId;

                sheet.Cell(rowNumber, 2).Value =
                    row.ObjectionNo;

                sheet.Cell(rowNumber, 3).Value =
                    row.AppealNo;

                sheet.Cell(rowNumber, 4).Value =
                    row.PropertyDescription;

                sheet.Cell(rowNumber, 5).Value =
                    row.Status;

                if (row.DateSent.HasValue)
                {
                    sheet.Cell(rowNumber, 6).Value =
                        row.DateSent.Value;

                    sheet.Cell(rowNumber, 6)
                        .Style.DateFormat.Format =
                        "dd MMM yyyy HH:mm";
                }

                sheet.Cell(rowNumber, 7).Value =
                    row.SentBy;

                rowNumber++;
            }

            /*
             * A formal Excel table is not created because row 2 must
             * remain blank between the headers and the first record.
             */
            sheet.Range(1, 1, 1, headers.Length)
                .SetAutoFilter();

            sheet.SheetView.FreezeRows(1);

            sheet.RangeUsed()
                ?.Style.Alignment.SetVertical(
                    XLAlignmentVerticalValues.Top);

            sheet.RangeUsed()
                ?.Style.Alignment.SetWrapText();

            sheet.Column(1).Width = 18;
            sheet.Column(2).Width = 24;
            sheet.Column(3).Width = 24;
            sheet.Column(4).Width = 48;
            sheet.Column(5).Width = 26;
            sheet.Column(6).Width = 22;
            sheet.Column(7).Width = 28;

            sheet.PageSetup.PageOrientation =
                XLPageOrientation.Landscape;

            sheet.PageSetup.FitToPages(1, 0);
        }

        private static void StyleSectionHeader(IXLRange range)
        {
            range.Style
                .Font.SetBold()
                .Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(XLColor.FromHtml("#1A8A4A"));
        }

        private static void StyleTableHeader(IXLRange range)
        {
            range.Style
                .Font.SetBold()
                .Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(XLColor.FromHtml("#111111"));
            range.Style.Alignment.SetHorizontal(
                XLAlignmentHorizontalValues.Center);
        }

        private static string MakeSafeFileName(string value)
        {
            var safe = Regex.Replace(
                value.Trim(),
                @"[^A-Za-z0-9_-]+",
                "_");

            safe = Regex.Replace(safe, @"_+", "_")
                .Trim('_');

            return string.IsNullOrWhiteSpace(safe)
                ? "Admin"
                : safe;
        }

        private static string NormalizeAdminKey(
            string? email,
            string? name)
        {
            if (!string.IsNullOrWhiteSpace(email))
            {
                return
                    $"EMAIL:{email.Trim().ToLowerInvariant()}";
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                return
                    $"NAME:{name.Trim().ToLowerInvariant()}";
            }

            return "UNASSIGNED";
        }

        private static bool IsStatus(
            string? actual,
            string expected)
        {
            return string.Equals(
                actual?.Trim(),
                expected,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string MapDisplayStatus(
            string? status)
        {
            if (IsStatus(status, "Sent"))
            {
                return "Notice-Sent";
            }

            if (IsStatus(status, "Email-Failed"))
            {
                return "Email-Failed";
            }

            if (IsStatus(status, "No-Owner-Email"))
            {
                return "No-Owner-Email";
            }

            if (IsStatus(status, "Printed"))
            {
                return "Printed / Awaiting Send";
            }

            return string.IsNullOrWhiteSpace(status)
                ? "Unknown"
                : status.Trim();
        }
    }
}