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
    private readonly EmailOptions _emailOptions;
    private readonly ILogger<ThirdPartyAppealStatsService> _logger;

    public ThirdPartyAppealStatsService(
        AppDbContext db,
        IConfiguration config,
        INoticeStatsEmailService statsEmail,
        IOptions<EmailOptions> emailOptions,
        ILogger<ThirdPartyAppealStatsService> logger)
    {
        _db = db;
        _config = config;
        _statsEmail = statsEmail;
        _emailOptions = emailOptions.Value;
        _logger = logger;
    }

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
                x.Valuer_Email,
                x.Premise_ID,
                x.Appeal_No,
                x.Property_Description,
                x.Status,
                x.SentAt,
                x.SentBy
            })
            .OrderBy(x => x.AdminName)
            .ThenBy(x => x.Appeal_No)
            .ToListAsync(ct);

        var valuationEnquiriesEmail =
            _config["TpaStats:ValuationEnquiriesEmail"]
                ?.Trim();

        if (string.IsNullOrWhiteSpace(
            valuationEnquiriesEmail))
        {
            valuationEnquiriesEmail =
                "ValuationEnquiries@joburg.org.za";
        }

        var adminGroups = rows
            .GroupBy(x =>
                NormalizeAdminKey(
                    x.AdminEmail,
                    x.AdminName))
            .Select(group =>
            {
                var first = group.First();

                var valuerEmails = group
                    .Select(x => x.Valuer_Email)
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim())
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();

                var ccEmails = new[]
                    {
                            valuationEnquiriesEmail
                    }
                    .Concat(valuerEmails)
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

                    ValuerEmails =
                        string.Join(
                            "; ",
                            valuerEmails),

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
                                    x.SentBy ?? ""
                            })
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
            CcEmails = admin.DefaultCcEmails
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
                ccEmails: admin.DefaultCcEmails,
                subject: subject,
                htmlBody: htmlBody,
                attachmentPath: result.ExcelPath,
                emlPath: result.EmlPath);

            await _statsEmail.SendStatsEmailAsync(
                admin.AdminEmail,
                admin.DefaultCcEmails,
                subject,
                htmlBody,
                result.ExcelPath,
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

    public async Task<ThirdPartyAppealStatsBulkSendResultVm> SendAllAdminReportsAsync(
        Guid workflowKey,
        string performedBy,
        CancellationToken ct)
    {
        var stats = await BuildStatsAsync(workflowKey, ct);

        var bulk = new ThirdPartyAppealStatsBulkSendResultVm
        {
            TotalAdmins = stats.Admins.Count
        };

        foreach (var admin in stats.Admins)
        {
            ct.ThrowIfCancellationRequested();

            if (!admin.HasAdminEmail)
            {
                bulk.Skipped++;

                var skipped = new ThirdPartyAppealStatsSendResultVm
                {
                    Success = false,
                    AdminName = admin.AdminName,
                    AdminEmail = admin.AdminEmail,
                    CcEmails = admin.DefaultCcEmails,
                    ErrorMessage = "Skipped because the Admin email is missing."
                };

                bulk.Results.Add(skipped);

                await WriteAuditAsync(
                    stats,
                    WorkflowAuditAction.TpaStatsEmailBlocked,
                    performedBy,
                    admin,
                    null,
                    null,
                    skipped.ErrorMessage,
                    ct);

                continue;
            }

            var result = await SendAdminReportAsync(
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
        var adminName = System.Net.WebUtility.HtmlEncode(
            string.IsNullOrWhiteSpace(admin.AdminName)
                ? "Admin"
                : admin.AdminName);

        return $"""
<!doctype html>
<html>
<body style="font-family:Arial,sans-serif;color:#222;line-height:1.6;">
    <p>Dear {adminName},</p>

    <p>
        Please find attached the Third-Party Appeal notice distribution
        report for the properties allocated to you.
    </p>

    <p><strong>Report summary:</strong></p>

    <table cellpadding="7" cellspacing="0"
           style="border-collapse:collapse;border:1px solid #d9d9d9;">
        <tr>
            <td style="border:1px solid #d9d9d9;"><strong>Total properties</strong></td>
            <td style="border:1px solid #d9d9d9;">{admin.TotalRecords:N0}</td>
        </tr>
        <tr>
            <td style="border:1px solid #d9d9d9;"><strong>Notices successfully sent</strong></td>
            <td style="border:1px solid #d9d9d9;">{admin.TotalSent:N0}</td>
        </tr>
        <tr>
            <td style="border:1px solid #d9d9d9;"><strong>Failed email attempts</strong></td>
            <td style="border:1px solid #d9d9d9;">{admin.TotalFailed:N0}</td>
        </tr>
        <tr>
            <td style="border:1px solid #d9d9d9;"><strong>Properties with no owner email</strong></td>
            <td style="border:1px solid #d9d9d9;">{admin.TotalNoEmail:N0}</td>
        </tr>
        <tr>
            <td style="border:1px solid #d9d9d9;"><strong>Notices awaiting sending</strong></td>
            <td style="border:1px solid #d9d9d9;">{admin.TotalPrinted:N0}</td>
        </tr>
    </table>

    <p>
        The Detail sheet in the attached workbook contains the Premise ID,
        Appeal Number, Property Description, notice status, date sent and
        the user who sent the notice.
    </p>

    <p>
        The successfully sent notices have been communicated to the relevant
        property owners. You may now proceed with the required scheduling
        activities on AKON.
    </p>

    <p>
        Please review any failed or missing-email records and take the
        necessary follow-up action.
    </p>

    <p>
        Kind regards,<br />
        <strong>Valuation Appeal Board Secretariat</strong><br />
        City of Johannesburg
    </p>
</body>
</html>
""";
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
        var fromAddress = string.IsNullOrWhiteSpace(
            _emailOptions.FromAddress)
            ? "no-reply@joburg.org.za"
            : _emailOptions.FromAddress;

        var fromName = string.IsNullOrWhiteSpace(
            _emailOptions.FromName)
            ? "Valuation Appeal Board Secretariat"
            : _emailOptions.FromName;

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
            CcEmails = admin.DefaultCcEmails,
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
        sheet.Cell("A5").Value = "Valuer Email(s)";
        sheet.Cell("B5").Value = admin.ValuerEmails;
        sheet.Cell("A6").Value = "CC Recipients";
        sheet.Cell("B6").Value = admin.DefaultCcEmails;

        sheet.Cell("A8").Value = "Roll";
        sheet.Cell("B8").Value =
            $"{stats.RollShortCode} — {stats.RollName}";
        sheet.Cell("A9").Value = "Valuation Period";
        sheet.Cell("B9").Value = stats.ValuationPeriod;
        sheet.Cell("A10").Value = "Version";
        sheet.Cell("B10").Value = stats.VersionText;
        sheet.Cell("A11").Value = "Report Generated";
        sheet.Cell("B11").Value = DateTime.Now;
        sheet.Cell("B11").Style.DateFormat.Format =
            "dd MMM yyyy HH:mm";

        sheet.Cell("A13").Value = "Summary";
        sheet.Range("A13:B13").Merge();
        StyleSectionHeader(sheet.Range("A13:B13"));

        sheet.Cell("A14").Value = "Total TPA Notices";
        sheet.Cell("B14").Value = admin.TotalRecords;
        sheet.Cell("A15").Value = "Notice-Sent";
        sheet.Cell("B15").Value = admin.TotalSent;
        sheet.Cell("A16").Value = "Printed / Awaiting Send";
        sheet.Cell("B16").Value = admin.TotalPrinted;
        sheet.Cell("A17").Value = "Email-Failed";
        sheet.Cell("B17").Value = admin.TotalFailed;
        sheet.Cell("A18").Value = "No-Owner-Email";
        sheet.Cell("B18").Value = admin.TotalNoEmail;

        sheet.Cell("A20").Value = "Status";
        sheet.Cell("B20").Value = "Count";
        StyleTableHeader(sheet.Range("A20:B20"));

        sheet.Cell("A21").Value = "Notice-Sent";
        sheet.Cell("B21").Value = admin.TotalSent;
        sheet.Cell("A22").Value = "Printed / Awaiting Send";
        sheet.Cell("B22").Value = admin.TotalPrinted;
        sheet.Cell("A23").Value = "Email-Failed";
        sheet.Cell("B23").Value = admin.TotalFailed;
        sheet.Cell("A24").Value = "No-Owner-Email";
        sheet.Cell("B24").Value = admin.TotalNoEmail;

        sheet.Range("A3:A11").Style.Font.SetBold();
        sheet.Range("A14:A18").Style.Font.SetBold();
        sheet.Range("B14:B18").Style
            .Font.SetBold()
            .Font.SetFontSize(12);

        sheet.Range("A3:B11").Style.Border
            .SetBottomBorder(XLBorderStyleValues.Thin)
            .Border.SetBottomBorderColor(XLColor.FromHtml("#E6E6E6"));

        sheet.Range("A14:B18").Style.Border
            .SetBottomBorder(XLBorderStyleValues.Thin)
            .Border.SetBottomBorderColor(XLColor.FromHtml("#E6E6E6"));

        sheet.Range("A20:B24").Style.Border
            .SetOutsideBorder(XLBorderStyleValues.Thin)
            .Border.SetInsideBorder(XLBorderStyleValues.Thin)
            .Border.SetOutsideBorderColor(XLColor.FromHtml("#D9D9D9"))
            .Border.SetInsideBorderColor(XLColor.FromHtml("#D9D9D9"));

        sheet.Cell("A26").Value =
            "The Detail sheet contains Premise ID, Appeal Number, Property Description, Status, Date Sent and Sent By.";
        sheet.Range("A26:D26").Merge();
        sheet.Range("A26:D26").Style
            .Font.SetItalic()
            .Font.SetFontColor(XLColor.FromHtml("#666666"));
        sheet.Range("A26:D26").Style.Alignment.SetWrapText();

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
                "Premise_ID",
                "Appeal_No",
                "Property_Desc",
                "Status",
                "Date Sent",
                "Sent By Who"
            };

        for (var column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
        }

        StyleTableHeader(sheet.Range(1, 1, 1, headers.Length));

        var rowNumber = 2;

        foreach (var row in admin.Details
            .OrderBy(x => x.AppealNo)
            .ThenBy(x => x.PremiseId))
        {
            sheet.Cell(rowNumber, 1).Value = row.PremiseId;
            sheet.Cell(rowNumber, 2).Value = row.AppealNo;
            sheet.Cell(rowNumber, 3).Value = row.PropertyDescription;
            sheet.Cell(rowNumber, 4).Value = row.Status;

            if (row.DateSent.HasValue)
            {
                sheet.Cell(rowNumber, 5).Value = row.DateSent.Value;
                sheet.Cell(rowNumber, 5).Style.DateFormat.Format =
                    "dd MMM yyyy HH:mm";
            }

            sheet.Cell(rowNumber, 6).Value = row.SentBy;
            rowNumber++;
        }

        if (rowNumber > 2)
        {
            var dataRange = sheet.Range(
                1,
                1,
                rowNumber - 1,
                headers.Length);

            var table = dataRange.CreateTable("TpaAdminNoticeDetails");
            table.Theme = XLTableTheme.TableStyleMedium4;
            table.ShowAutoFilter = true;
        }

        sheet.SheetView.FreezeRows(1);
        sheet.RangeUsed()?.Style.Alignment.SetVertical(
            XLAlignmentVerticalValues.Top);
        sheet.RangeUsed()?.Style.Alignment.SetWrapText();

        sheet.Column(1).Width = 18;
        sheet.Column(2).Width = 24;
        sheet.Column(3).Width = 48;
        sheet.Column(4).Width = 26;
        sheet.Column(5).Width = 22;
        sheet.Column(6).Width = 28;

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

