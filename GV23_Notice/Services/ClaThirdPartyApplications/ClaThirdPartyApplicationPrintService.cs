using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models.Workflow.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services.ClaThirdPartyApplications
{
    public sealed class ClaThirdPartyApplicationPrintService
        : IClaThirdPartyApplicationPrintService
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly IClaThirdPartyFormalNoticePdfService _pdf;
        private readonly IClaThirdPartyAppealPackZipService _appealPackZip;

        public ClaThirdPartyApplicationPrintService(
            AppDbContext db,
            IConfiguration config,
            IClaThirdPartyFormalNoticePdfService pdf
            , IClaThirdPartyAppealPackZipService appealPackZip)
        {
            _db = db;
            _config = config;
            _pdf = pdf;
            _appealPackZip = appealPackZip;
        }

        public async Task<ThirdPartyAppealPrintVm> BuildPrintVmAsync(
            Guid key,
            CancellationToken ct)
        {
            var settings = await GetSettingsAsync(key, ct);

            if (settings.Notice != NoticeKind.CLA_TPA)
                throw new InvalidOperationException(
                    "This print dashboard is only for CLA Third-Party Application notices.");

            var query = await BuildNoticeQueryAsync(settings, false, ct);

            var rows = await query
                .Select(x => new
                {
                    x.Id,
                    x.ClaNumber,
                    x.ObjectionNumber,
                    x.PropertyDescription,
                    x.RollCategory1,
                    x.OwnerEmail,
                    x.Status,
                    x.PdfPath,
                    x.EmailError
                })
                .ToListAsync(ct);

            var groups = rows
                .GroupBy(x => NormalizeGroup(x.RollCategory1))
                .OrderBy(x => x.Key)
                .Select(g => new ThirdPartyAppealPrintGroupVm
                {
                    GroupName = g.Key,
                    Total = g.Count(),
                    PendingPrint = g.Count(x => IsPendingPrint(x.Status, x.PdfPath)),
                    Printed = g.Count(x => IsPrinted(x.Status, x.PdfPath)),
                    PrintFailed = g.Count(x => x.Status == "Print-Failed"),
                    MissingOwnerEmail = g.Count(x => string.IsNullOrWhiteSpace(x.OwnerEmail))
                })
                .ToList();

            return new ThirdPartyAppealPrintVm
            {
                WorkflowKey = key,
                SettingsId = settings.Id,
                TotalRecords = rows.Count,
                TotalPendingPrint = rows.Count(x => IsPendingPrint(x.Status, x.PdfPath)),
                TotalPrinted = rows.Count(x => IsPrinted(x.Status, x.PdfPath)),
                TotalPrintFailed = rows.Count(x => x.Status == "Print-Failed"),
                TotalMissingOwnerEmail = rows.Count(x => string.IsNullOrWhiteSpace(x.OwnerEmail)),
                Groups = groups,
                Items = rows
                    .OrderBy(x => x.ClaNumber)
                    .Take(20)
                    .Select(x => new ThirdPartyAppealPrintItemVm
                    {
                        Id = x.Id,
                        AppealNo = x.ClaNumber ?? "",
                        ObjectionNo = x.ObjectionNumber ?? "",
                        PropertyDescription = x.PropertyDescription ?? "",
                        PropertyType = NormalizeGroup(x.RollCategory1),
                        OwnerEmail = x.OwnerEmail ?? "",
                        Status = string.IsNullOrWhiteSpace(x.Status) ? "Pending" : x.Status,
                        PdfPath = x.PdfPath ?? "",
                        ErrorMessage = x.EmailError ?? ""
                    })
                    .ToList()
            };
        }

        public async Task<ThirdPartyAppealPrintResultVm> PrintAsync(
            Guid key,
            string printedBy,
            bool forceReprint,
            CancellationToken ct)
        {
            var settings = await GetSettingsAsync(key, ct);

            if (settings.Notice != NoticeKind.CLA_TPA)
                throw new InvalidOperationException(
                    "This print service is only for CLA Third-Party Application notices.");

            if (!settings.IsApproved)
                throw new InvalidOperationException(
                    "Step 2 must be approved before printing.");

            var query = await BuildNoticeQueryAsync(settings, true, ct);

            if (!forceReprint)
            {
                query = query.Where(x =>
                    x.Status == null ||
                    x.Status == "" ||
                    x.Status == "Imported" ||
                    x.Status == "Pending" ||
                    x.Status == "Preview-Approved" ||
                    x.Status == "Print-Failed" ||
                    string.IsNullOrWhiteSpace(x.PdfPath));
            }

            var notices = await query
                .OrderBy(x => x.ClaNumber)
                .ToListAsync(ct);

            var result = new ThirdPartyAppealPrintResultVm
            {
                Total = notices.Count
            };

            if (notices.Count == 0)
            {
                result.ErrorMessage =
                    "No CLA Third-Party Application records are available to print.";
                return result;
            }

            foreach (var notice in notices)
            {
                try
                {
                    ValidateForPrint(notice);
                    LinkToWorkflow(notice, settings, printedBy);

                    notice.Status = "Printing";
                    notice.EmailError = null;
                    notice.UpdatedAtUtc = DateTime.UtcNow;
                    notice.UpdatedBy = printedBy;
                    await _db.SaveChangesAsync(ct);

                    var folder = BuildClaPdfFolder(settings, notice);
                    Directory.CreateDirectory(folder);

                    var pdfBytes = _pdf.BuildPdf(settings, notice);

                    var prefix =
                        _config["Storage:CLAThirdPartyAppealApplication:PdfFilePrefix"]
                        ?? "CLA Valuation Appeal formal notice";

                    var pdfPath = Path.Combine(
                        folder,
                        $"{prefix.Trim()} {SafeFile(notice.ClaNumber!)}.pdf");

                    await File.WriteAllBytesAsync(pdfPath, pdfBytes, ct);

                    notice.PdfPath = pdfPath;
                    notice.PdfExists = File.Exists(pdfPath);

                    if (!notice.PdfExists)
                    {
                        throw new IOException(
                            $"The CLA notice PDF was not created: {pdfPath}");
                    }

                    /*
                     * Build the appeal-pack ZIP before marking the record as Printed.
                     * AppealPackPath remains the original source folder/file.
                     */
                    var appealPackZipPath =
                        await _appealPackZip.BuildAppealPackZipAsync(
                            settings,
                            notice,
                            folder,
                            ct);

                    if (string.IsNullOrWhiteSpace(appealPackZipPath) ||
                        !File.Exists(appealPackZipPath))
                    {
                        throw new IOException(
                            $"The CLA appeal-pack ZIP was not created for '{notice.ClaNumber}'.");
                    }

                    // Keep AppealPackPath as the original source location.
                    // Store the generated archive in AppealPackZipPath.
                    notice.AppealPackZipPath = appealPackZipPath;
                    notice.AppealPackFileName =
                        Path.GetFileName(appealPackZipPath);

                    notice.AppealPackExists = true;

                    notice.Status = "Printed";
                    notice.PrintedAtUtc = DateTime.UtcNow;
                    notice.PrintedBy = printedBy;
                    notice.UpdatedAtUtc = DateTime.UtcNow;
                    notice.UpdatedBy = printedBy;
                    notice.EmailError = null;

                    result.Printed++;
                }
                catch (Exception ex)
                {
                    notice.NoticeSettingsId = settings.Id;
                    notice.Status = "Print-Failed";
                    notice.EmailError = ex.Message;
                    notice.UpdatedAtUtc = DateTime.UtcNow;
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
            var query = await BuildNoticeQueryAsync(settings, false, ct);

            return new ThirdPartyAppealPrintProgressVm
            {
                Total = await query.CountAsync(ct),
                Pending = await query.CountAsync(x =>
                    x.Status == null ||
                    x.Status == "" ||
                    x.Status == "Imported" ||
                    x.Status == "Pending" ||
                    x.Status == "Preview-Approved", ct),
                Printed = await query.CountAsync(x =>
                    x.Status == "Printed" ||
                    x.Status == "Sent" ||
                    x.Status == "Email-Failed", ct),
                Failed = await query.CountAsync(x =>
                    x.Status == "Print-Failed", ct)
            };
        }

        private async Task<IQueryable<ClaThirdPartyApplicationNotice>>
            BuildNoticeQueryAsync(
                NoticeSettings settings,
                bool tracking,
                CancellationToken ct)
        {
            IQueryable<ClaThirdPartyApplicationNotice> query =
                tracking
                    ? _db.ClaThirdPartyApplicationNotices
                    : _db.ClaThirdPartyApplicationNotices.AsNoTracking();

            query = query.Where(x => x.IsActive);

            var hasLinkedRows = await query.AnyAsync(
                x => x.NoticeSettingsId == settings.Id,
                ct);

            if (hasLinkedRows)
            {
                return query.Where(x =>
                    x.NoticeSettingsId == settings.Id);
            }

            /*
             * New workflow versions receive a new NoticeSettingsId. The imported
             * CLA rows may still point to the previous version, so fall back to
             * active records for the same roll. PrintAsync will relink them to
             * the current settings before changing their status.
             */
            return query.Where(x =>
                x.RollId == null ||
                x.RollId == settings.RollId);
        }

        private async Task<NoticeSettings> GetSettingsAsync(
            Guid key,
            CancellationToken ct)
        {
            return await _db.NoticeSettings
                .FirstOrDefaultAsync(
                    x => x.ApprovalKey == key ||
                         x.WorkflowKey == key,
                    ct)
                ?? throw new InvalidOperationException(
                    "Workflow settings were not found.");
        }

        private void LinkToWorkflow(
            ClaThirdPartyApplicationNotice notice,
            NoticeSettings settings,
            string updatedBy)
        {
            notice.NoticeSettingsId = settings.Id;
            notice.RollId ??= settings.RollId;
            notice.RollShortCode = FirstNonEmpty(
                notice.RollShortCode,
                settings.Roll.ToString());

            notice.ValuationPeriod = FirstNonEmpty(
                notice.ValuationPeriod,
                settings.RollName,
                settings.ValuationPeriodCode,
                "General Valuation Roll 2023");

            notice.LetterDate = settings.LetterDate.Date;
            notice.RepresentationCloseDate =
                settings.ObjectionEndDate ??
                settings.LetterDate.AddDays(30);

            notice.UpdatedAtUtc = DateTime.UtcNow;
            notice.UpdatedBy = updatedBy;
        }

        private string BuildClaPdfFolder(
            NoticeSettings settings,
            ClaThirdPartyApplicationNotice notice)
        {
            if (string.IsNullOrWhiteSpace(notice.ClaNumber))
                throw new InvalidOperationException("CLA number is missing.");

            var rootKey = ResolveRootKey(
                notice.RollShortCode,
                settings.Roll.ToString());

            var appealRoot =
                _config[$"Storage:AppealRootsByShortCode:{rootKey}"];

            if (string.IsNullOrWhiteSpace(appealRoot))
                throw new InvalidOperationException(
                    $"Appeal root path is missing for key '{rootKey}'.");

            var folderName =
                _config["Storage:CLAThirdPartyAppealApplication:PdfFolderName"]
                ?? "CLA-THIRD-PARTY APPLICATION";

            return Path.Combine(
                appealRoot,
                SafeFolder(notice.ClaNumber),
                folderName.Trim());
        }

        private static void ValidateForPrint(
            ClaThirdPartyApplicationNotice notice)
        {
            if (string.IsNullOrWhiteSpace(notice.ClaNumber))
                throw new InvalidOperationException("CLA number is missing.");

            if (string.IsNullOrWhiteSpace(notice.OwnerName))
                throw new InvalidOperationException(
                    $"Owner name is missing for CLA '{notice.ClaNumber}'.");

            if (string.IsNullOrWhiteSpace(notice.PropertyDescription))
                throw new InvalidOperationException(
                    $"Property description is missing for CLA '{notice.ClaNumber}'.");
        }

        private static bool IsPendingPrint(string? status, string? pdfPath) =>
            string.IsNullOrWhiteSpace(status) ||
            status == "Imported" ||
            status == "Pending" ||
            status == "Preview-Approved" ||
            status == "Print-Failed" ||
            string.IsNullOrWhiteSpace(pdfPath);

        private static bool IsPrinted(string? status, string? pdfPath) =>
            (status == "Printed" ||
             status == "Sent" ||
             status == "Email-Failed") &&
            !string.IsNullOrWhiteSpace(pdfPath);

        private static string NormalizeGroup(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unknown";

            return value.Equals(
                "Multi",
                StringComparison.OrdinalIgnoreCase)
                ? "Multipurpose"
                : value.Trim();
        }

        private static string ResolveRootKey(
            string? recordShortCode,
            string? settingsRoll)
        {
            var value = FirstNonEmpty(
                    recordShortCode,
                    settingsRoll,
                    "GV23")
                .Replace(" ", "")
                .ToUpperInvariant();

            return value switch
            {
                "SUPP1" => "SUPP 1",
                "SUPP2" => "SUPP 2",
                "SUPP3" => "SUPP 3",
                _ => "GV23"
            };
        }

        private static string FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x))?.Trim() ?? "";

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
