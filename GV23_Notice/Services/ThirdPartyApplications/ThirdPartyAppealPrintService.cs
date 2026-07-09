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
                x.Status == "Print-Failed",
                ct);

            var items = await query
                .OrderBy(x => x.Appeal_No)
                .Take(200)
                .Select(x => new ThirdPartyAppealPrintItemVm
                {
                    Id = x.Id,
                    AppealNo = x.Appeal_No,
                    ObjectionNo = x.Objection_No ?? "",
                    PropertyDescription = x.Property_Description ?? "",
                    PropertyType = x.Property_Type ?? "",
                    OwnerEmail = x.OwnerEmail ?? "",
                    Status = x.Status,
                    PdfPath = x.PdfPath ?? "",
                    ErrorMessage = x.ErrorMessage ?? ""
                })
                .ToListAsync(ct);

            return new ThirdPartyAppealPrintVm
            {
                NoticeSettingsId = settings.Id,
                RollId = settings.RollId,
                RollShortCode = await GetRollShortCodeAsync(settings.RollId, ct),
                ValuationPeriod = settings.ValuationPeriodCode ?? "",
                Notice = "Third-Party Appeal Application",

                TotalRecords = total,
                TotalPendingPrint = pending,
                TotalPrinted = printed,
                TotalPrintFailed = failed,
                PreviewApproved = settings.IsApproved,
                Items = items
            };
        }

        public async Task<ThirdPartyAppealPrintResultVm> PrintAsync(
            Guid key,
            string printedBy,
            CancellationToken ct)
        {
            var settings = await GetSettingsAsync(key, ct);

            if (settings.Notice != NoticeKind.TPA)
                throw new InvalidOperationException("This print service is only for Third-Party Appeal Application notices.");

            if (!settings.IsApproved)
                throw new InvalidOperationException("Step 2 must be approved before printing.");

            var notices = await BuildNoticeQuery(settings)
                .Where(x =>
                    x.Status == "Pending" ||
                    x.Status == "Preview-Approved" ||
                    x.Status == "Print-Failed" ||
                    string.IsNullOrWhiteSpace(x.Status))
                .OrderBy(x => x.Appeal_No)
                .ToListAsync(ct);

            var result = new ThirdPartyAppealPrintResultVm
            {
                Total = notices.Count
            };

            foreach (var notice in notices)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(notice.Appeal_No))
                        throw new InvalidOperationException("Appeal_No is missing.");

                    var folder = BuildThirdPartyFolder(settings, notice, ct);
                    Directory.CreateDirectory(folder);

                    notice.NoticeSettingsId = settings.Id;
                    notice.RollId = settings.RollId;
                    notice.ValuationPeriod = settings.ValuationPeriodCode ?? notice.ValuationPeriod;
                    notice.LetterDate = DateTime.Today;

                    var pdfBytes = _pdf.BuildPdf(settings, notice);

                    var pdfPath = Path.Combine(
                        folder,
                        $"Valuation Appeal Board formal notice{SafeFile(notice.Appeal_No)}.pdf");

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
                    notice.ErrorMessage = null;

                    result.Printed++;
                }
                catch (Exception ex)
                {
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

            return q.Where(x => x.RollId == settings.RollId);
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

        private async Task<string> GetRollShortCodeAsync(
            int rollId,
            CancellationToken ct)
        {
            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.RollId == rollId, ct);

            return roll?.ShortCode ?? "";
        }

        private string BuildThirdPartyFolder(
            NoticeSettings settings,
            ThirdPartyAppealApplicationNotice notice,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(notice.Appeal_No))
                throw new InvalidOperationException("Appeal_No is required.");

            var root = ResolveAppealRoot(settings, ct);
            var appealFolder = Path.Combine(root, notice.Appeal_No);

            return Path.Combine(appealFolder, "THIRD-PARTY APPLICATION");
        }

        private string ResolveAppealFolder(
            NoticeSettings settings,
            string appealNo,
            CancellationToken ct)
        {
            var root = ResolveAppealRoot(settings, ct);
            return Path.Combine(root, appealNo);
        }

        private string ResolveAppealRoot(
            NoticeSettings settings,
            CancellationToken ct)
        {
            var roll = _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefault(x => x.RollId == settings.RollId);

            if (roll == null)
                throw new InvalidOperationException($"Roll not found for RollId {settings.RollId}.");

            var shortCode = roll.ShortCode ?? "";

            var root = _config[$"Storage:AppealRootsByShortCode:{shortCode}"];

            if (string.IsNullOrWhiteSpace(root))
                root = _config[$"Storage:AppealRootsByShortCode:{shortCode.Replace(" ", "")}"];

            if (string.IsNullOrWhiteSpace(root))
                throw new InvalidOperationException($"Appeal root is not configured for roll '{shortCode}'.");

            return root;
        }

        private static string SafeFile(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unknown";

            var invalid = Path.GetInvalidFileNameChars();

            return new string(
                value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        }
    }
}