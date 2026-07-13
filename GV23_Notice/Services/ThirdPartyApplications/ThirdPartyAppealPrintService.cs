using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models.Workflow.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services.ThirdPartyApplications
{
    public sealed class ThirdPartyAppealPrintService : IThirdPartyAppealPrintService
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly IThirdPartyAppealFormalNoticePdfService _pdf;
        private readonly IThirdPartyAppealPackZipService _zip;

        public ThirdPartyAppealPrintService(
            AppDbContext db,
            IConfiguration config,
            IThirdPartyAppealFormalNoticePdfService pdf,
            IThirdPartyAppealPackZipService zip)
        {
            _db = db;
            _config = config;
            _pdf = pdf;
            _zip = zip;
        }

        public async Task<ThirdPartyAppealPrintVm> BuildPrintVmAsync(
     Guid workflowKey,
     CancellationToken ct)
        {
            var settings = await GetSettingsAsync(workflowKey, ct);

            if (settings.Notice != NoticeKind.TPA)
                throw new InvalidOperationException("This print dashboard is only for Third-Party Appeal Application notices.");

            var hasLinkedRows = await _db.ThirdPartyAppealApplicationNotices
                .AsNoTracking()
                .AnyAsync(x => x.NoticeSettingsId == settings.Id, ct);

            var query = _db.ThirdPartyAppealApplicationNotices
                .AsNoTracking()
                .AsQueryable();

            if (hasLinkedRows)
                query = query.Where(x => x.NoticeSettingsId == settings.Id);

            var rows = await query
                .Select(x => new
                {
                    x.Id,
                    x.Appeal_No,
                    x.Objection_No,
                    x.Property_Description,
                    x.Property_Type,
                    x.OwnerEmail,
                    x.Status,
                    x.PdfPath,
                    x.ErrorMessage
                })
                .ToListAsync(ct);

            var totalRecords = rows.Count;

            var totalPendingPrint = rows.Count(x =>
                string.IsNullOrWhiteSpace(x.Status) ||
                x.Status == "Pending" ||
                x.Status == "Preview-Approved" ||
                x.Status == "Print-Failed" ||
                string.IsNullOrWhiteSpace(x.PdfPath));

            var totalPrinted = rows.Count(x =>
                (x.Status == "Printed" || x.Status == "Sent" || x.Status == "Email-Failed") &&
                !string.IsNullOrWhiteSpace(x.PdfPath));

            var totalPrintFailed = rows.Count(x => x.Status == "Print-Failed");

            var totalMissingOwnerEmail = rows.Count(x => string.IsNullOrWhiteSpace(x.OwnerEmail));

            /*
             * Group for dashboard.
             * Use PropertyType because it is useful and avoids listing every record.
             * If later you want VAB grouping, change GroupName to x.VabName / x.Appeal_Board.
             */
            var groups = rows
                .GroupBy(x => NormalizeGroup(x.Property_Type))
                .OrderBy(g => g.Key)
                .Select(g => new ThirdPartyAppealPrintGroupVm
                {
                    GroupName = g.Key,
                    Total = g.Count(),
                    PendingPrint = g.Count(x =>
                        string.IsNullOrWhiteSpace(x.Status) ||
                        x.Status == "Pending" ||
                        x.Status == "Preview-Approved" ||
                        x.Status == "Print-Failed" ||
                        string.IsNullOrWhiteSpace(x.PdfPath)),
                    Printed = g.Count(x =>
                        (x.Status == "Printed" || x.Status == "Sent" || x.Status == "Email-Failed") &&
                        !string.IsNullOrWhiteSpace(x.PdfPath)),
                    PrintFailed = g.Count(x => x.Status == "Print-Failed"),
                    MissingOwnerEmail = g.Count(x => string.IsNullOrWhiteSpace(x.OwnerEmail))
                })
                .ToList();

            return new ThirdPartyAppealPrintVm
            {
                WorkflowKey = workflowKey,
                SettingsId = settings.Id,

                TotalRecords = totalRecords,
                TotalPendingPrint = totalPendingPrint,
                TotalPrinted = totalPrinted,
                TotalPrintFailed = totalPrintFailed,
                TotalMissingOwnerEmail = totalMissingOwnerEmail,

                Groups = groups,

                // Do not load 1000+ rows into the view. Keep sample only if needed.
                Items = rows
                    .OrderBy(x => x.Appeal_No)
                    .Take(20)
                    .Select(x => new ThirdPartyAppealPrintItemVm
                    {
                        Id = x.Id,
                        AppealNo = x.Appeal_No ?? "",
                        ObjectionNo = x.Objection_No ?? "",
                        PropertyDescription = x.Property_Description ?? "",
                        PropertyType = x.Property_Type ?? "",
                        OwnerEmail = x.OwnerEmail ?? "",
                        Status = string.IsNullOrWhiteSpace(x.Status) ? "Pending" : x.Status,
                        PdfPath = x.PdfPath ?? "",
                        ErrorMessage = x.ErrorMessage ?? ""
                    })
                    .ToList()
            };
        }

        private static string NormalizeGroup(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unknown";

            var cleaned = value.Trim();

            if (cleaned.Equals("Multi", StringComparison.OrdinalIgnoreCase))
                return "Multipurpose";

            return cleaned;
        }

        public async Task<ThirdPartyAppealPrintResultVm> PrintAsync(
      Guid key,
      string printedBy,
      bool forceReprint,
      CancellationToken ct)
        {
            var settings = await GetSettingsAsync(key, ct);

            if (settings.Notice != NoticeKind.TPA)
                throw new InvalidOperationException("This print service is only for Third-Party Appeal Application notices.");

            if (!settings.IsApproved)
                throw new InvalidOperationException("Step 2 must be approved before printing.");

            /*
             * TPA does not use NoticeBatches.
             * Some imported records may not yet have NoticeSettingsId.
             * If linked rows exist, use only linked rows.
             * If none exist, use the imported table and link records while printing.
             */
            var hasLinkedRows = await _db.ThirdPartyAppealApplicationNotices
                .AnyAsync(x => x.NoticeSettingsId == settings.Id, ct);

            var query = _db.ThirdPartyAppealApplicationNotices
                .AsQueryable();

            if (hasLinkedRows)
            {
                query = query.Where(x => x.NoticeSettingsId == settings.Id);
            }

            if (forceReprint)
            {
                /*
                 * Reprint all records for this workflow/extract.
                 * This is needed when the user starts a new Step 1 / new version
                 * but the TPA extract records were already printed before.
                 */
                query = query.Where(x =>
                    x.Status == null ||
                    x.Status == "" ||
                    x.Status == "Pending" ||
                    x.Status == "Preview-Approved" ||
                    x.Status == "Print-Failed" ||
                    x.Status == "Printed" ||
                    x.Status == "Email-Failed" ||
                    x.Status == "Sent");
            }
            else
            {
                query = query.Where(x =>
                    x.Status == null ||
                    x.Status == "" ||
                    x.Status == "Pending" ||
                    x.Status == "Preview-Approved" ||
                    x.Status == "Print-Failed" ||
                    string.IsNullOrWhiteSpace(x.PdfPath));
            }

            var notices = await query
                .OrderBy(x => x.Appeal_No)
                .ToListAsync(ct);

            var result = new ThirdPartyAppealPrintResultVm
            {
                Total = notices.Count
            };

            if (notices.Count == 0)
            {
                result.ErrorMessage =
                    "No Third-Party Appeal Application records are available to print. Check that the extract has been loaded into ThirdPartyAppealApplicationNotices.";
                return result;
            }

            var activeRolls = await _db.RollRegistry
                .AsNoTracking()
                .Where(x => x.IsActive)
                .ToListAsync(ct);

            foreach (var notice in notices)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(notice.Appeal_No))
                        throw new InvalidOperationException("Appeal_No is missing.");

                    /*
                     * Link this imported record to the current workflow.
                     * This makes future dashboard/progress queries accurate.
                     */
                    var resolvedRoll = ResolveRollFromAppealNumber(
                        notice.Appeal_No,
                        activeRolls);

                    notice.NoticeSettingsId = settings.Id;
                    notice.RollId = resolvedRoll.RollId;
                    notice.RollShortCode = resolvedRoll.ShortCode;
                    notice.ValuationPeriod = resolvedRoll.Name;
                    notice.LetterDate = DateTime.Today;

                    notice.Status = "Printing";
                    notice.UpdatedAt = DateTime.Now;
                    notice.UpdatedBy = printedBy;
                    notice.ErrorMessage = null;

                    await _db.SaveChangesAsync(ct);

                    var folder = BuildThirdPartyFolder(settings, notice, ct);
                    Directory.CreateDirectory(folder);

                    var pdfBytes = _pdf.BuildPdf(settings, notice);

                    var pdfPrefix =
               _config["Storage:ThirdPartyAppealApplication:PdfFilePrefix"]
               ?? "Valuation Appeal Board formal notice";

                    var pdfPath = Path.Combine(
                        folder,
                        $"{pdfPrefix.TrimEnd()} {SafeFile(notice.Appeal_No)}.pdf");

                    await File.WriteAllBytesAsync(pdfPath, pdfBytes, ct);

                    var zipPath = await _zip.BuildAppealPackZipAsync(
                        settings,
                        notice.Appeal_No,
                        folder,
                        ct);

                    notice.PdfPath = pdfPath;
                    notice.AppealPackFolderPath = ResolveAppealFolder(settings, notice.Appeal_No, ct);
                    notice.AppealPackZipPath = zipPath;

                    notice.Status = "Printed";
                    notice.PrintedAt = DateTime.Now;
                    notice.PrintedBy = printedBy;
                    notice.UpdatedAt = DateTime.Now;
                    notice.UpdatedBy = printedBy;
                    notice.ErrorMessage = null;

                    result.Printed++;
                }
                catch (Exception ex)
                {
                    notice.NoticeSettingsId = settings.Id;

                    notice.Status = "Print-Failed";
                    notice.ErrorMessage = ex.Message;
                    notice.UpdatedAt = DateTime.Now;
                    notice.UpdatedBy = printedBy;

                    result.Failed++;
                }

                await _db.SaveChangesAsync(ct);
            }

            return result;
        }

        public async Task<ThirdPartyAppealPrintProgressVm> GetProgressAsync(
            Guid key,
            CancellationToken ct)
        {
            var settings = await GetSettingsAsync(key, ct);

            var query = BuildNoticeQuery(settings);

            var total = await query.CountAsync(ct);
            var printed = await query.CountAsync(x => x.Status == "Printed" || x.Status == "Sent", ct);
            var failed = await query.CountAsync(x => x.Status == "Print-Failed", ct);
            var pending = await query.CountAsync(x =>
                x.Status == "Pending" ||
                x.Status == "Preview-Approved" ||
                string.IsNullOrWhiteSpace(x.Status),
                ct);

            return new ThirdPartyAppealPrintProgressVm
            {
                Total = total,
                Pending = pending,
                Printed = printed,
                Failed = failed
            };
        }

        private IQueryable<ThirdPartyAppealApplicationNotice> BuildNoticeQuery(
         NoticeSettings settings)
        {
            var q = _db.ThirdPartyAppealApplicationNotices.AsQueryable();

            if (settings.Id > 0)
            {
                var bySettings = q.Where(x => x.NoticeSettingsId == settings.Id);

                if (bySettings.Any())
                    return bySettings;
            }

            /*
             * TPA records are selected by extract/workflow first.
             * If no rows are linked yet, return all imported TPA records.
             * They will be linked during print.
             */
            return q;
        }

        private async Task<NoticeSettings> GetSettingsAsync(
            Guid key,
            CancellationToken ct)
        {
            var settings = await _db.NoticeSettings
                .FirstOrDefaultAsync(x => x.ApprovalKey == key || x.WorkflowKey == key, ct);

            if (settings == null)
                throw new InvalidOperationException("Workflow settings were not found.");

            return settings;
        }



        private string BuildThirdPartyFolder(
          NoticeSettings settings,
          ThirdPartyAppealApplicationNotice notice,
          CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(notice.Appeal_No))
                throw new InvalidOperationException("Appeal_No is missing.");

            var rootKey = ResolveAppealRootKeyFromAppealNo(notice.Appeal_No);

            var appealRoot = _config[$"Storage:AppealRootsByShortCode:{rootKey}"];

            if (string.IsNullOrWhiteSpace(appealRoot))
            {
                throw new InvalidOperationException(
                    $"Appeal root path is missing in appsettings for key '{rootKey}'.");
            }

            var tpaFolderName =
                _config["Storage:ThirdPartyAppealApplication:PdfFolderName"]
                ?? "THIRD-PARTY APPLICATION";

            return Path.Combine(
                appealRoot,
                SafeFolder(notice.Appeal_No),
                tpaFolderName);
        }
        private string ResolveAppealFolder(
    NoticeSettings settings,
    string appealNo,
    CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(appealNo))
                throw new InvalidOperationException("Appeal_No is missing.");

            var rootKey = ResolveAppealRootKeyFromAppealNo(appealNo);

            var appealRoot = _config[$"Storage:AppealRootsByShortCode:{rootKey}"];

            if (string.IsNullOrWhiteSpace(appealRoot))
            {
                throw new InvalidOperationException(
                    $"Appeal root path is missing in appsettings for key '{rootKey}'.");
            }

            return Path.Combine(
                appealRoot,
                SafeFolder(appealNo));
        }


      
        private static string ResolveAppealRootKeyFromAppealNo(string? appealNo)
        {
            if (string.IsNullOrWhiteSpace(appealNo))
                return "GV23";

            var value = appealNo.Trim();

            if (value.Contains("APP-GV23-Sup1", StringComparison.OrdinalIgnoreCase))
                return "SUPP 1";

            if (value.Contains("APP-GV23-Sup2", StringComparison.OrdinalIgnoreCase))
                return "SUPP 2";

            if (value.Contains("APP-GV23-Sup3", StringComparison.OrdinalIgnoreCase))
                return "SUPP 3";

            if (value.Contains("APP-GV23-", StringComparison.OrdinalIgnoreCase))
                return "GV23";

            return "GV23";
        }
        private static Domain.Rolls.RollRegistry ResolveRollFromAppealNumber(
            string appealNo,
            IReadOnlyCollection<Domain.Rolls.RollRegistry> activeRolls)
        {
            var expectedShortCode = ResolveRollShortCodeFromAppealNumber(
                appealNo);

            var roll = activeRolls.FirstOrDefault(x =>
                NormalizeRollShortCode(x.ShortCode) ==
                NormalizeRollShortCode(expectedShortCode));

            return roll
                ?? throw new InvalidOperationException(
                    $"No active RollRegistry entry was found for Appeal_No '{appealNo}' using ShortCode '{expectedShortCode}'.");
        }

        private static string ResolveRollShortCodeFromAppealNumber(
            string appealNo)
        {
            if (string.IsNullOrWhiteSpace(appealNo))
            {
                throw new InvalidOperationException(
                    "Appeal_No is required to resolve the valuation roll.");
            }

            var value = appealNo.Trim();

            if (value.StartsWith(
                "APP-GV23-Sup3-",
                StringComparison.OrdinalIgnoreCase))
            {
                return "SUPP 3";
            }

            if (value.StartsWith(
                "APP-GV23-Sup2-",
                StringComparison.OrdinalIgnoreCase))
            {
                return "SUPP 2";
            }

            if (value.StartsWith(
                "APP-GV23-Sup1-",
                StringComparison.OrdinalIgnoreCase))
            {
                return "SUPP 1";
            }

            if (value.StartsWith(
                "APP-GV23-",
                StringComparison.OrdinalIgnoreCase))
            {
                return "GV23";
            }

            throw new InvalidOperationException(
                $"Appeal_No '{appealNo}' does not match a supported TPA roll pattern.");
        }

        private static string NormalizeRollShortCode(string? shortCode)
        {
            return (shortCode ?? "")
                .Replace(" ", "")
                .Trim()
                .ToUpperInvariant();
        }

        private static string SafeFolder(string value)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');

            return value.Trim();
        }



        private static string SafeFile(string value)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');

            return value.Trim();
        }

    }
}