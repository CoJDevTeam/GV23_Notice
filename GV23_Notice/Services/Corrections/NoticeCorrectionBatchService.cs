using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models.Corrections.ViewModels;
using GV23_Notice.Models.Workflow.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services.Corrections
{
    public sealed class NoticeCorrectionBatchService : INoticeCorrectionBatchService
    {
        private const string RequiredCc = "ValuationEnquiries@joburg.org.za";

        private readonly AppDbContext _db;

        public NoticeCorrectionBatchService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<int> CreateBatchAsync(
            CorrectionPreviewVm vm,
            string createdBy,
            CancellationToken ct)
        {
            if (vm == null)
                throw new ArgumentNullException(nameof(vm));

            if (vm.Items == null || vm.Items.Count == 0)
                throw new InvalidOperationException("No correction items were found. Search again before creating the correction batch.");

            var sourceNotice = vm.SourceNotice;
            var printNotice = vm.PrintNotice;

            var firstItem = vm.Items.First();

            var batchName = await BuildBatchNameAsync(printNotice, vm.RollShortCode, ct);

            var printNoticeKind = printNotice.ToString();
            var sourceNoticeKind = sourceNotice.ToString();
            var printNoticeTitle = NoticeDisplayName(printNotice);

            var template = await _db.NoticeCorrectionEmailTemplates
                .AsNoTracking()
                .Where(x => x.NoticeKind == printNoticeKind
                         && x.IsDefault
                         && x.IsActive)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            var subject = template?.SubjectTemplate
                ?? $"Corrected {printNoticeTitle} Notice – {{PropertyDesc}}";

            var body = template?.BodyTemplate
                ?? "Dear Client,\n\nPlease find attached the corrected notice for your attention.\n\nRegards,\nCity of Johannesburg";

            var cc = string.IsNullOrWhiteSpace(template?.CcTemplate)
                ? RequiredCc
                : EnsureValuationEnquiriesCc(template.CcTemplate);

            subject = ApplyTokens(subject, vm, firstItem);
            body = ApplyTokens(body, vm, firstItem);
            cc = ApplyTokens(cc, vm, firstItem);
            cc = EnsureValuationEnquiriesCc(cc);

            var batch = new NoticeCorrectionBatch
            {
                CorrectionBatchName = batchName,

                RollId = vm.RollId,
                RollShortCode = vm.RollShortCode,
                SourceDb = vm.SourceDb,

                // Keep NoticeKind for backward compatibility, but use PrintNoticeKind as the real print notice.
                NoticeKind = printNoticeKind,
                NoticeSubKind = vm.NoticeSubKind,

                SourceNoticeKind = sourceNoticeKind,
                PrintNoticeKind = printNoticeKind,
                PrintNoticeTitle = printNoticeTitle,

                ReferenceType = vm.ReferenceType,
                ReferenceNo = vm.ReferenceNo,

                CorrectionReason = vm.CorrectionReason,

                CreatedBy = createdBy,
                CreatedAt = DateTime.Now,
                Status = "Batch-Created"
            };

            foreach (var src in vm.Items)
            {
                var itemSubject = ApplyTokens(subject, vm, src);
                var itemBody = ApplyTokens(body, vm, src);
                var itemCc = EnsureValuationEnquiriesCc(ApplyTokens(cc, vm, src));

                var item = new NoticeCorrectionItem
                {
                    CorrectionBatch = batch,

                    RollId = vm.RollId,
                    RollShortCode = vm.RollShortCode,
                    SourceDb = vm.SourceDb,

                    // Keep NoticeKind for compatibility. Use PrintNoticeKind for actual printing.
                    NoticeKind = printNoticeKind,
                    NoticeSubKind = vm.NoticeSubKind,

                    SourceNoticeKind = sourceNoticeKind,
                    PrintNoticeKind = printNoticeKind,
                    PrintNoticeTitle = printNoticeTitle,

                    ReferenceType = vm.ReferenceType,
                    ReferenceNo = vm.ReferenceNo,

                    ObjectionNo = src.ObjectionNo,
                    AppealNo = src.AppealNo,
                    QueryNo = src.QueryNo,
                    ReviewNo = src.ReviewNo,

                    PremiseId = src.PremiseId,
                    UnitKey = src.UnitKey,
                    ValuationKey = src.ValuationKey,

                    PropertyDesc = src.PropertyDesc,
                    PropertyType = src.PropertyType,

                    // Important: this must be the real Objector_Type.
                    RecipientRole = src.RecipientRole,
                    RecipientName = src.RecipientName,
                    RecipientEmail = src.RecipientEmail,

                    ADDR1 = src.ADDR1,
                    ADDR2 = src.ADDR2,
                    ADDR3 = src.ADDR3,
                    ADDR4 = src.ADDR4,
                    ADDR5 = src.ADDR5,

                    OldCategory = src.OldCategory,
                    OldCategory2 = src.OldCategory2,
                    OldCategory3 = src.OldCategory3,

                    OldMarketValue = src.OldMarketValue,
                    OldMarketValue2 = src.OldMarketValue2,
                    OldMarketValue3 = src.OldMarketValue3,

                    OldExtent = src.OldExtent,
                    OldExtent2 = src.OldExtent2,
                    OldExtent3 = src.OldExtent3,

                    NewCategory = src.NewCategory,
                    NewCategory2 = src.NewCategory2,
                    NewCategory3 = src.NewCategory3,

                    NewMarketValue = src.NewMarketValue,
                    NewMarketValue2 = src.NewMarketValue2,
                    NewMarketValue3 = src.NewMarketValue3,

                    NewExtent = src.NewExtent,
                    NewExtent2 = src.NewExtent2,
                    NewExtent3 = src.NewExtent3,

                    WEFDate = src.WEFDate,
                    WEFDate2 = src.WEFDate2,
                    WEFDate3 = src.WEFDate3,

                    BatchDate = src.BatchDate,
                    LetterDate = src.LetterDate,
                    ClosingDate = src.ClosingDate,
                    AppealStartDate = src.AppealStartDate,
                    AppealCloseDate = src.AppealCloseDate,

                    Section51Pin = src.Section51Pin,
                    Section52Review = src.Section52Review,

                    EmailSubject = itemSubject,
                    EmailBody = itemBody,
                    EmailCc = itemCc,

                    SnapshotJson = src.SnapshotJson,

                    Status = "Pending",
                    CreatedAt = DateTime.Now
                };

                batch.Items.Add(item);
            }

            _db.NoticeCorrectionBatches.Add(batch);
            await _db.SaveChangesAsync(ct);

            return batch.Id;
        }

        private async Task<string> BuildBatchNameAsync(
            NoticeKind printNotice,
            string rollShortCode,
            CancellationToken ct)
        {
            var today = DateTime.Now.ToString("yyyyMMdd");

            var cleanRoll = (rollShortCode ?? "ROLL")
                .Replace(" ", "")
                .Replace("-", "")
                .ToUpperInvariant();

            var noticeCode = printNotice switch
            {
                NoticeKind.S53Rev => "S53REV",
                _ => printNotice.ToString().ToUpperInvariant()
            };

            var prefix = $"CORR_{noticeCode}_{cleanRoll}_{today}_";

            var count = await _db.NoticeCorrectionBatches
                .AsNoTracking()
                .CountAsync(x => x.CorrectionBatchName.StartsWith(prefix), ct);

            return $"{prefix}{count + 1:0000}";
        }

        private static string EnsureValuationEnquiriesCc(string? cc)
        {
            if (string.IsNullOrWhiteSpace(cc))
                return RequiredCc;

            var emails = cc
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (!emails.Any(x => string.Equals(x, RequiredCc, StringComparison.OrdinalIgnoreCase)))
                emails.Add(RequiredCc);

            return string.Join("; ", emails);
        }

        private static string ApplyTokens(
            string value,
            CorrectionPreviewVm vm,
            CorrectionPreviewItemVm item)
        {
            return (value ?? "")
                .Replace("{PropertyDesc}", item.PropertyDesc ?? "")
                .Replace("{ReferenceNo}", vm.ReferenceNo ?? "")
                .Replace("{ObjectionNo}", item.ObjectionNo ?? "")
                .Replace("{AppealNo}", item.AppealNo ?? "")
                .Replace("{PremiseId}", item.PremiseId ?? "")
                .Replace("{ValuationKey}", item.ValuationKey ?? "")
                .Replace("{ObjectorType}", item.RecipientRole ?? "")
                .Replace("{RecipientEmail}", item.RecipientEmail ?? "")
                .Replace("{SourceNotice}", vm.SourceNoticeText ?? "")
                .Replace("{PrintNotice}", vm.PrintNoticeText ?? "");
        }

        private static string NoticeDisplayName(NoticeKind notice)
        {
            return notice switch
            {
                NoticeKind.S49 => "Section 49",
                NoticeKind.S51 => "Section 51",
                NoticeKind.S52 => "Section 52",
                NoticeKind.S53 => "Section 53 MVD",
                NoticeKind.S53Rev => "Section 53 Revised MVD",
                NoticeKind.DJ => "Dear Johnny",
                NoticeKind.IN => "Invalid Notice",
                NoticeKind.S78 => "Section 78",
                _ => notice.ToString()
            };
        }
    }
}