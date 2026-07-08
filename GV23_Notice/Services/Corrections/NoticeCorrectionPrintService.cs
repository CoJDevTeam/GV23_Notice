using GV23_Notice.Data;
using GV23_Notice.Domain.Rolls;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Services.Notices.DearJohnny;
using GV23_Notice.Services.Notices.Invalidity;
using GV23_Notice.Services.Notices.Section49;
using GV23_Notice.Services.Notices.Section51;
using GV23_Notice.Services.Notices.Section52;
using GV23_Notice.Services.Notices.Section53;
using GV23_Notice.Services.Notices.Section53.COJ_Notice_2026.Models.ViewModels.Section53;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services.Corrections
{
    public sealed class NoticeCorrectionPrintService : INoticeCorrectionPrintService
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;

        private readonly ISection49PdfBuilder _s49;
        private readonly ISection51PdfBuilder _s51;
        private readonly ISection52PdfService _s52;
        private readonly ISection53PdfService _s53;
        private readonly IDearJonnyPdfService _dj;
        private readonly IInvalidNoticePdfService _invalid;

        private readonly IWebHostEnvironment _env;

        public NoticeCorrectionPrintService(
            AppDbContext db,
            IConfiguration config,
            ISection49PdfBuilder s49,
            ISection51PdfBuilder s51,
            ISection52PdfService s52,
            ISection53PdfService s53,
            IDearJonnyPdfService dj,
            IInvalidNoticePdfService invalid,
            IWebHostEnvironment env)
        {
            _db = db;
            _config = config;

            _s49 = s49;
            _s51 = s51;
            _s52 = s52;
            _s53 = s53;
            _dj = dj;
            _invalid = invalid;

            _env = env;
        }

        public async Task PrintBatchAsync(int batchId, string printedBy, CancellationToken ct)
        {
            var batch = await _db.NoticeCorrectionBatches
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == batchId, ct);

            if (batch == null)
                throw new InvalidOperationException($"Correction batch {batchId} was not found.");

            if (batch.Items == null || batch.Items.Count == 0)
                throw new InvalidOperationException("This correction batch has no correction items to print.");

            foreach (var item in batch.Items)
            {
                try
                {
                    var pdfPath = BuildCorrectionPdfPath(batch, item);

                    await PrintOneCorrectionNoticeAsync(batch, item, pdfPath, ct);

                    item.PdfPath = pdfPath;
                    item.PrintedAt = DateTime.Now;

                    // Corrections go into the normal QA flow after printing.
                    item.Status = "QA-Pending";

                    item.ErrorMessage = null;
                }
                catch (Exception ex)
                {
                    item.Status = "Failed";
                    item.ErrorMessage = ex.Message;
                }
            }

            var hasFailed = batch.Items.Any(x => x.Status == "Failed");
            var hasQaPending = batch.Items.Any(x => x.Status == "QA-Pending");
            var hasPrintedFile = batch.Items.Any(x => !string.IsNullOrWhiteSpace(x.PdfPath));

            batch.PrintedBy = printedBy;
            batch.PrintedAt = DateTime.Now;

            batch.Status = hasFailed && (hasQaPending || hasPrintedFile)
                ? "QA-Pending-With-Errors"
                : hasFailed
                    ? "Failed"
                    : "QA-Pending";

            await _db.SaveChangesAsync(ct);
        }

        private async Task PrintOneCorrectionNoticeAsync(
            NoticeCorrectionBatch batch,
            NoticeCorrectionItem item,
            string pdfPath,
            CancellationToken ct)
        {
            var printKind = item.PrintNoticeKind ?? batch.PrintNoticeKind ?? item.NoticeKind;
            var sourceKind = item.SourceNoticeKind ?? batch.SourceNoticeKind ?? item.NoticeKind;

            if (string.IsNullOrWhiteSpace(printKind))
                throw new InvalidOperationException("PrintNoticeKind is missing.");

            if (string.IsNullOrWhiteSpace(sourceKind))
                throw new InvalidOperationException("SourceNoticeKind is missing.");

            /*
             * IMPORTANT BUSINESS RULE:
             *
             * SourceNoticeKind controls where the correction values came from.
             * PrintNoticeKind controls which REAL PDF service/body/heading to use.
             *
             * Example:
             * SourceNoticeKind = S53Rev
             * PrintNoticeKind  = S53
             *
             * Result:
             * Use corrected revised values already stored on NoticeCorrectionItem,
             * but generate the PDF using the normal Section 53 PDF service/body.
             *
             * Email is NOT handled here.
             * Email later uses NoticeCorrectionItem.EmailSubject / EmailBody / EmailCc.
             */

            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.RollId == batch.RollId, ct);

            var pdfBytes = printKind switch
            {
                "S49" => BuildSection49CorrectionPdf(batch, item, roll),
                "S51" => BuildSection51CorrectionPdf(batch, item, roll),
                "S52" => BuildSection52CorrectionPdf(batch, item),

                // S53 normal body/heading
                "S53" => BuildSection53CorrectionPdf(
                    batch,
                    item,
                    roll,
                    isRevisedBody: false),

                // S53 revised body/heading
                "S53Rev" => BuildSection53CorrectionPdf(
                    batch,
                    item,
                    roll,
                    isRevisedBody: true),

                "DJ" => BuildDearJohnnyCorrectionPdf(batch, item, roll),
                "IN" => BuildInvalidCorrectionPdf(batch, item),

                _ => throw new InvalidOperationException($"Correction printing for {printKind} is not implemented yet.")
            };

            if (pdfBytes == null || pdfBytes.Length == 0)
                throw new InvalidOperationException($"The {printKind} PDF service returned an empty PDF.");

            Directory.CreateDirectory(Path.GetDirectoryName(pdfPath)!);
            await File.WriteAllBytesAsync(pdfPath, pdfBytes, ct);
        }

        private byte[] BuildSection53CorrectionPdf(
            NoticeCorrectionBatch batch,
            NoticeCorrectionItem item,
            RollRegistry? roll,
            bool isRevisedBody)
        {
            var row = new Section53MvdRow
            {
                ObjectionNo = item.ObjectionNo ?? item.ReferenceNo,
                ValuationKey = item.ValuationKey,

                Email = item.RecipientEmail,
                ObjectorName = item.RecipientName,

                Addr1 = item.ADDR1,
                Addr2 = item.ADDR2,
                Addr3 = item.ADDR3,
                Addr4 = item.ADDR4,
                Addr5 = item.ADDR5,

                PropertyDesc = item.PropertyDesc,

                AppealCloseDate = item.AppealCloseDate,
                Section52Review = item.Section52Review,

                IsMulti =
                    HasAnyValue(item.NewCategory2, item.NewMarketValue2, item.NewExtent2) ||
                    HasAnyValue(item.NewCategory3, item.NewMarketValue3, item.NewExtent3),

                RollName = roll?.Name ?? batch.RollShortCode ?? "Valuation Roll",

                Gv_Category = item.OldCategory,
                Gv_Category2 = item.OldCategory2,
                Gv_Category3 = item.OldCategory3,

                Gv_Market_Value = item.OldMarketValue,
                Gv_Market_Value2 = item.OldMarketValue2,
                Gv_Market_Value3 = item.OldMarketValue3,

                Gv_Extent = item.OldExtent,
                Gv_Extent2 = item.OldExtent2,
                Gv_Extent3 = item.OldExtent3,

                // Corrected values always go into the MVD side.
                // The selected PrintNoticeKind decides whether the PDF service prints
                // normal Section 53 body or revised Section 53 body.
                Mvd_Category = item.NewCategory,
                Mvd_Category2 = item.NewCategory2,
                Mvd_Category3 = item.NewCategory3,

                Mvd_Market_Value = item.NewMarketValue,
                Mvd_Market_Value2 = item.NewMarketValue2,
                Mvd_Market_Value3 = item.NewMarketValue3,

                Mvd_Extent = item.NewExtent,
                Mvd_Extent2 = item.NewExtent2,
                Mvd_Extent3 = item.NewExtent3,

                WEFMVD = item.WEFDate,
                WEFMVD2 = item.WEFDate2,
                WEFMVD3 = item.WEFDate3,

                /*
                 * Critical:
                 * This must follow PrintNoticeKind, not SourceNoticeKind.
                 *
                 * Source S53Rev + Print S53    => IsRevisedMvd = false
                 * Source S53    + Print S53    => IsRevisedMvd = false
                 * Source S53Rev + Print S53Rev => IsRevisedMvd = true
                 * Source S53    + Print S53Rev => IsRevisedMvd = true
                 */
                IsRevisedMvd = isRevisedBody,

                OriginalS53BatchDate = item.BatchDate,
                RevisedMvdBatchDate = item.BatchDate
            };

            var letterDate = DateOnly.FromDateTime(item.LetterDate ?? batch.CreatedAt);

            return _s53.BuildNoticePdf(row, letterDate);
        }

        private byte[] BuildSection51CorrectionPdf(
            NoticeCorrectionBatch batch,
            NoticeCorrectionItem item,
            RollRegistry? roll)
        {
            var data = new Section51NoticeData
            {
                RollName = roll?.Name ?? batch.RollShortCode ?? "",
                ObjectionNo = item.ObjectionNo ?? item.ReferenceNo ?? "",
                Section51Pin = item.Section51Pin,

                Addr1 = item.ADDR1,
                Addr2 = item.ADDR2,
                Addr3 = item.ADDR3,
                Addr4 = item.ADDR4,
                Addr5 = item.ADDR5,

                Email = item.RecipientEmail,
                OwnerName = item.RecipientName,

                ValuationKey = item.ValuationKey,
                PropertyDesc = item.PropertyDesc ?? "",

                IsMulti =
                    HasAnyValue(item.NewCategory2, item.NewMarketValue2, item.NewExtent2) ||
                    HasAnyValue(item.NewCategory3, item.NewMarketValue3, item.NewExtent3),

                Section6 = new Section6Row
                {
                    Old_Category = item.OldCategory,
                    Old2_Category = item.OldCategory2,
                    Old3_Category = item.OldCategory3,

                    Old_Market_Value = item.OldMarketValue,
                    Old2_Market_Value = item.OldMarketValue2,
                    Old3_Market_Value = item.OldMarketValue3,

                    Old_Extent = item.OldExtent,
                    Old2_Extent = item.OldExtent2,
                    Old3_Extent = item.OldExtent3,

                    New_Category = item.NewCategory,
                    New2_Category = item.NewCategory2,
                    New3_Category = item.NewCategory3,

                    New_Market_Value = item.NewMarketValue,
                    New2_Market_Value = item.NewMarketValue2,
                    New3_Market_Value = item.NewMarketValue3,

                    New_Extent = item.NewExtent,
                    New2_Extent = item.NewExtent2,
                    New3_Extent = item.NewExtent3,

                    WithEffectDate = item.WEFDate
                }
            };

            var ctx = new Section51NoticeContext
            {
                HeaderImagePath = ResolveHeaderImagePath(),
                LetterDate = item.LetterDate ?? batch.CreatedAt,
                SubmissionsCloseDate = item.ClosingDate ?? item.AppealCloseDate ?? batch.CreatedAt.AddDays(30),
                PortalUrl = _config["Email:PortalUrl"] ?? "https://objections.joburg.org.za/",
                SignOffName = "S. Faiaz",
                SignOffTitle = "Municipal Valuer"
            };

            return _s51.BuildNotice(data, ctx);
        }

        private byte[] BuildSection52CorrectionPdf(
            NoticeCorrectionBatch batch,
            NoticeCorrectionItem item)
        {
            var row = new AppealDecisionRow
            {
                ADDR1 = item.ADDR1,
                ADDR2 = item.ADDR2,
                ADDR3 = item.ADDR3,
                ADDR4 = item.ADDR4,
                ADDR5 = item.ADDR5,

                Email = item.RecipientEmail,
                Property_desc = item.PropertyDesc,

                Objection_No = item.ObjectionNo,
                Appeal_No = item.AppealNo,
                valuation_Key = item.ValuationKey,

                App_Category = item.NewCategory,
                App_Category2 = item.NewCategory2,
                App_Category3 = item.NewCategory3,

                App_Market_Value = item.NewMarketValue,
                App_Market_Value2 = item.NewMarketValue2,
                App_Market_Value3 = item.NewMarketValue3,

                App_Extent = item.NewExtent,
                App_Extent2 = item.NewExtent2,
                App_Extent3 = item.NewExtent3
            };

            var ctx = new Section52PdfContext
            {
                HeaderImagePath = ResolveHeaderImagePath(),
                LetterDate = DateOnly.FromDateTime(item.LetterDate ?? batch.CreatedAt)
            };

            return _s52.BuildNotice(row, ctx);
        }

        private byte[] BuildDearJohnnyCorrectionPdf(
            NoticeCorrectionBatch batch,
            NoticeCorrectionItem item,
            RollRegistry? roll)
        {
            var data = new DearJonnyPdfData
            {
                RollName = roll?.Name ?? batch.RollShortCode ?? "",
                ObjectionNo = item.ObjectionNo ?? item.ReferenceNo ?? "",
                PropertyDescription = item.PropertyDesc ?? "",

                Addr1 = item.ADDR1 ?? "",
                Addr2 = item.ADDR2 ?? "",
                Addr3 = item.ADDR3 ?? "",
                Addr4 = item.ADDR4 ?? "",
                Addr5 = item.ADDR5 ?? "",

                ValuationKey = item.ValuationKey ?? "",
                RecipientName = item.RecipientName
            };

            var ctx = new DearJonnyPdfContext
            {
                HeaderImagePath = ResolveHeaderImagePath(),
                LetterDate = item.LetterDate ?? batch.CreatedAt
            };

            return _dj.BuildNotice(data, ctx);
        }

        private byte[] BuildInvalidCorrectionPdf(
            NoticeCorrectionBatch batch,
            NoticeCorrectionItem item)
        {
            var data = new InvalidNoticePdfData
            {
                Kind = InvalidNoticeKind.InvalidObjection,

                ObjectionNo = item.ObjectionNo ?? item.ReferenceNo ?? "",
                PropertyDescription = item.PropertyDesc ?? "",

                RecipientName = item.RecipientName,
                RecipientAddress = BuildAddressLine(item),

                Addr1 = item.ADDR1,
                Addr2 = item.ADDR2,
                Addr3 = item.ADDR3,
                Addr4 = item.ADDR4,
                Addr5 = item.ADDR5,

                ValuationKey = item.ValuationKey ?? ""
            };

            var ctx = new InvalidNoticePdfContext
            {
                HeaderImagePath = ResolveHeaderImagePath(),
                LetterDate = item.LetterDate ?? batch.CreatedAt
            };

            return _invalid.BuildNotice(data, ctx);
        }

        private byte[] BuildSection49CorrectionPdf(
            NoticeCorrectionBatch batch,
            NoticeCorrectionItem item,
            RollRegistry? roll)
        {
            var rows = new List<Section49PropertyRow>();

            if (HasAnyValue(item.NewCategory, item.NewMarketValue, item.NewExtent))
            {
                rows.Add(new Section49PropertyRow
                {
                    Category = item.NewCategory ?? "",
                    MarketValue = FormatMoney(item.NewMarketValue),
                    Extent = item.NewExtent ?? "",
                    Remarks = ""
                });
            }

            if (HasAnyValue(item.NewCategory2, item.NewMarketValue2, item.NewExtent2))
            {
                rows.Add(new Section49PropertyRow
                {
                    Category = item.NewCategory2 ?? "",
                    MarketValue = FormatMoney(item.NewMarketValue2),
                    Extent = item.NewExtent2 ?? "",
                    Remarks = ""
                });
            }

            if (HasAnyValue(item.NewCategory3, item.NewMarketValue3, item.NewExtent3))
            {
                rows.Add(new Section49PropertyRow
                {
                    Category = item.NewCategory3 ?? "",
                    MarketValue = FormatMoney(item.NewMarketValue3),
                    Extent = item.NewExtent3 ?? "",
                    Remarks = ""
                });
            }

            var data = new Section49PdfData
            {
                Addr1 = item.ADDR1 ?? "",
                Addr2 = item.ADDR2 ?? "",
                Addr3 = item.ADDR3 ?? "",
                Addr4 = item.ADDR4 ?? "",
                Addr5 = item.ADDR5 ?? "",

                PropertyDesc = item.PropertyDesc ?? "",
                PhysicalAddress = BuildAddressLine(item),
                ValuationKey = item.ValuationKey ?? "",

                ForceFourRows = rows.Count > 1,
                PropertyRows = rows
            };

            var letterDate = item.LetterDate ?? batch.CreatedAt;

            var ctx = new Section49NoticeContext
            {
                HeaderImagePath = ResolveHeaderImagePath(),
                LetterDate = letterDate,
                InspectionStartDate = item.AppealStartDate ?? letterDate,
                InspectionEndDate = item.AppealCloseDate ?? letterDate.AddDays(30),
                ExtendedEndDate = item.ClosingDate,
                RollHeaderText = roll?.Name ?? batch.RollShortCode ?? "",
                FinancialYearsText = "2023 to 2027"
            };

            return _s49.BuildNotice(data, ctx);
        }

        private string BuildCorrectionPdfPath(
            NoticeCorrectionBatch batch,
            NoticeCorrectionItem item)
        {
            var root = ResolveRollRoot(batch.RollShortCode);

            var printKind =
                item.PrintNoticeKind
                ?? batch.PrintNoticeKind
                ?? item.NoticeKind;

            var noticeFolder = ResolveNoticeFolder(printKind);

            var referenceFolder = SafeFile(
                item.ObjectionNo
                ?? item.ReferenceNo
                ?? batch.ReferenceNo);

            var batchFolder = SafeFile(batch.CorrectionBatchName);

            var folder = Path.Combine(
                root,
                referenceFolder,
                "Corrections",
                noticeFolder,
                batchFolder);

            Directory.CreateDirectory(folder);

            var printTitle =
                item.PrintNoticeTitle
                ?? batch.PrintNoticeTitle
                ?? NoticeDisplayName(printKind);

            var fileName =
                $"{SafeFile(item.ObjectionNo ?? item.ReferenceNo)}_" +
                $"{SafeFile(item.PropertyDesc)}_" +
                $"Corrected_{SafeFile(printTitle)}_" +
                $"{SafeFile(item.RecipientRole)}.pdf";

            return Path.Combine(folder, fileName);
        }

        private string ResolveRollRoot(string? rollShortCode)
        {
            if (string.IsNullOrWhiteSpace(rollShortCode))
                throw new InvalidOperationException("RollShortCode is missing. Cannot build correction PDF folder.");

            var root = _config[$"Storage:ObjectionRootsByShortCode:{rollShortCode}"];

            if (string.IsNullOrWhiteSpace(root))
            {
                var cleanRoll = rollShortCode.Replace(" ", "");
                root = _config[$"Storage:ObjectionRootsByShortCode:{cleanRoll}"];
            }

            if (string.IsNullOrWhiteSpace(root))
            {
                throw new InvalidOperationException(
                    $"No storage root configured for roll '{rollShortCode}'. Check Storage:ObjectionRootsByShortCode in appsettings.");
            }

            return root;
        }

        private string ResolveNoticeFolder(string? noticeKind)
        {
            if (string.IsNullOrWhiteSpace(noticeKind))
                throw new InvalidOperationException("Notice kind is missing. Cannot resolve notice folder.");

            var folder = _config[$"Storage:NoticeTypeFolders:{noticeKind}"];

            if (!string.IsNullOrWhiteSpace(folder))
                return folder;

            return noticeKind switch
            {
                "S49" => "Section 49",
                "S51" => "Section 51",
                "S52" => "Section 52",
                "S53" => "Section 53 MVD",
                "S53Rev" => "Section 53 Revised MVD",
                "DJ" => "Dear Johnny",
                "IN" => "Invalid Notice",
                "S78" => "Section 78",
                _ => noticeKind
            };
        }

        private string ResolveHeaderImagePath()
        {
            var relative =
                _config["Section53Pdf:HeaderImageRelativePath"]
                ?? _config["NoticePdf:HeaderImageRelativePath"]
                ?? "Images/Obj_Header.PNG";

            if (Path.IsPathRooted(relative))
                return relative;

            return Path.Combine(_env.WebRootPath ?? "", relative);
        }

        private static string BuildAddressLine(NoticeCorrectionItem item)
        {
            return string.Join("\n", new[]
            {
                item.ADDR1,
                item.ADDR2,
                item.ADDR3,
                item.ADDR4,
                item.ADDR5
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static string NoticeDisplayName(string? noticeKind)
        {
            return noticeKind switch
            {
                "S49" => "Section 49",
                "S51" => "Section 51",
                "S52" => "Section 52",
                "S53" => "Section 53 MVD",
                "S53Rev" => "Section 53 Revised MVD",
                "DJ" => "Dear Johnny",
                "IN" => "Invalid Notice",
                "S78" => "Section 78",
                _ => noticeKind ?? "Unknown"
            };
        }

        private static string FormatMoney(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            var clean = value
                .Replace("R", "", StringComparison.OrdinalIgnoreCase)
                .Replace(" ", "")
                .Replace(",", "")
                .Trim();

            return decimal.TryParse(clean, out var amount)
                ? $"R {amount:N0}"
                : value;
        }

        private static bool HasAnyValue(params string?[] values)
        {
            return values.Any(x => !string.IsNullOrWhiteSpace(x));
        }

        private static string SafeFile(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unknown";

            var invalid = Path.GetInvalidFileNameChars();

            var cleaned = new string(
                value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());

            cleaned = cleaned
                .Replace("/", "_")
                .Replace("\\", "_")
                .Replace(":", "_")
                .Replace("*", "_")
                .Replace("?", "_")
                .Replace("\"", "_")
                .Replace("<", "_")
                .Replace(">", "_")
                .Replace("|", "_")
                .Trim();

            while (cleaned.Contains("  "))
                cleaned = cleaned.Replace("  ", " ");

            if (cleaned.Length > 120)
                cleaned = cleaned.Substring(0, 120).Trim();

            return cleaned;
        }
    }
}