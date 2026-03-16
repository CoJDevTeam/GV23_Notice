using GV23_Notice.Data;
using GV23_Notice.Domain.Rolls;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models.DTOs;
using GV23_Notice.Services.Email;
using GV23_Notice.Services.Notices.DearJohnny;
using GV23_Notice.Services.Notices.Invalidity;
using GV23_Notice.Services.Notices.Section49;
using GV23_Notice.Services.Notices.Section51;
using GV23_Notice.Services.Notices.Section52;
using GV23_Notice.Services.Notices.Section53;
using GV23_Notice.Services.Notices.Section53.COJ_Notice_2026.Models.ViewModels.Section53;
using GV23_Notice.Services.Notices.Section78;
using GV23_Notice.Services.Preview;
using GV23_Notice.Services.Preview.GV23_Notice.Services.Notices;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Xml.Linq;

namespace GV23_Notice.Services.Notices
{
    public sealed class NoticePreviewService : INoticePreviewService
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly IPreviewDbDataService _previewDb;
        private readonly INoticeEmailTemplateService _email;

        private readonly ISection49PdfBuilder _s49;
        private readonly ISection51PdfBuilder _s51;
        private readonly ISection52PdfService _s52;
        private readonly ISection53PdfService _s53;
        private readonly IDearJonnyPdfService _dj;
        private readonly IInvalidNoticePdfService _inv;

        public NoticePreviewService(
            AppDbContext db,
            IWebHostEnvironment env,
            IPreviewDbDataService previewDb,
            ISection49PdfBuilder s49,
            ISection51PdfBuilder s51,
            ISection52PdfService s52,
            ISection53PdfService s53,
            IDearJonnyPdfService dj,
            IInvalidNoticePdfService inv,
            INoticeEmailTemplateService email)
        {
            _db = db;
            _env = env;
            _previewDb = previewDb;

            _s49 = s49;
            _s51 = s51;
            _s52 = s52;
            _s53 = s53;
            _dj = dj;
            _inv = inv;
            _email = email;
        }

        public async Task<NoticePreviewResult> BuildPreviewAsync(
      int settingsId,
      PreviewVariant variant,
      PreviewMode mode,
      string? appealNo,
      CancellationToken ct)
        {
            var settings = await _db.NoticeSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == settingsId, ct);

            if (settings is null)
                throw new InvalidOperationException("NoticeSettings not found.");

            if (!settings.IsApproved)
                throw new InvalidOperationException("Step1 settings must be approved before Step2 preview.");

            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.RollId == settings.RollId, ct);

            var rollName = await _db.RollRegistry
    .Where(r => r.RollId == r.RollId)
    .Select(r => r.Name)
    .FirstOrDefaultAsync(ct) ?? "Valuation Roll";

            if (roll is null)
                throw new InvalidOperationException("RollRegistry not found.");

            var headerPath = Path.Combine(_env.WebRootPath, "Images", "Obj_Header.PNG");

            // Mode flags
            var isSplitPdf = mode == PreviewMode.SplitPdf;
            var isEmailMulti = mode == PreviewMode.EmailMulti;
            var rollNames = settings.RollName ?? roll.Name ?? "Valuation Roll";
            // =========================
            // 1) Load DB data per notice
            // =========================
            byte[] pdfBytes;
            string pdfFileName;

            // For email
            string recipientName;
            string recipientEmail;
            string addressLine;

            string sampleObjectionNo = "";
            string sampleAppealNo = "";
            string valuationKey = "";
            string samplePropertyDesc = ""; // ✅ NEW

            switch (settings.Notice)
            {
                case NoticeKind.S49:
                    {
                        var db = await _previewDb.S49PreviewDbDataAsync(roll.RollId, split: isSplitPdf, ct);

                        valuationKey = db.ValuationKey ?? "";
                        // ✅ S49 doesn't have PropertyDesc, use addresses as "property"
                        samplePropertyDesc = FirstNonEmpty(db.PropertyDesc, db.LisStreetAddress, "");

                        var ctx = new Section49NoticeContext
                        {
                            HeaderImagePath = headerPath,
                            SignaturePath = settings.SignaturePath,
                            LetterDate = settings.LetterDate,
                            InspectionStartDate = settings.ObjectionStartDate ?? settings.LetterDate,
                            InspectionEndDate = settings.ObjectionEndDate ?? settings.LetterDate.AddDays(30),
                            ExtendedEndDate = settings.ExtensionDate,
                            FinancialYearsText = settings.FinancialYearsText ?? "",
                            RollHeaderText = rollNames   // ← ADD THIS
                        };

                        var data = MapS49ToPdf(db, forceFourRows: isSplitPdf);

                        pdfBytes = _s49.BuildNotice(data, ctx);
                        pdfFileName = $"{roll.ShortCode}_S49_PREVIEW.pdf";

                        recipientName = FirstNonEmpty(db.Addr1, "Property Owner");
                        recipientEmail = db.Email ?? "";
                        addressLine = FirstNonEmpty(db.PremiseAddress, db.LisStreetAddress, "");
                        sampleObjectionNo = "";
                        sampleAppealNo = "";
                        break;
                    }

                case NoticeKind.S51:
                    {
                        var preferMulti = isSplitPdf;
                        var db = await _previewDb.S51PreviewDbDataAsync(roll.RollId, preferMulti, ct);

                        sampleObjectionNo = db.ObjectionNo ?? "";
                        valuationKey = ""; // add later if in query
                        samplePropertyDesc = db.PropertyDesc ?? ""; // ✅ NEW

                        var ctx = new Section51NoticeContext
                        {
                            HeaderImagePath = headerPath,
                            LetterDate = settings.LetterDate,
                            SubmissionsCloseDate = (settings.EvidenceCloseDate ?? settings.LetterDate.AddDays(30)),
                            PortalUrl = string.IsNullOrWhiteSpace(settings.PortalUrl) ? "https://objections.joburg.org.za/" : settings.PortalUrl,
                            SignOffName = "S. Faiaz",
                            SignOffTitle = "Municipal Valuer"
                        };

                        var data = MapS51ToPdf(db, roll.Name ?? "");
                        pdfBytes = _s51.BuildNotice(data, ctx);
                        pdfFileName = $"{roll.ShortCode}_S51_PREVIEW.pdf";

                        recipientName = FirstNonEmpty(db.Addr1, "Property Owner");
                        recipientEmail = db.Email ?? "";
                        addressLine = BuildAddrLine(db.Addr2, db.Addr3, db.Addr4, db.Addr5);
                        sampleAppealNo = "";
                        break;
                    }

                case NoticeKind.S52:
                    {
                        var isReview = (variant == PreviewVariant.S52ReviewDecision);

                        // appealNo may be blank when using date-range preview (backlog flow).
                        // PreviewDbDataService routes to the ByRange SPs automatically when blank.
                        var db = await _previewDb.S52PreviewDbDataAsync(roll.RollId, appealNo ?? "", isReview, ct);

                        sampleAppealNo = db.AppealNo ?? appealNo ?? "";
                        sampleObjectionNo = db.ObjectionNo ?? "";
                        valuationKey = db.ValuationKey ?? "";
                        samplePropertyDesc = db.PropertyDesc ?? ""; // ✅ NEW

                        var ctx = new Section52PdfContext
                        {
                            HeaderImagePath = headerPath,
                            LetterDate = DateOnly.FromDateTime(settings.LetterDate)
                        };

                        var row = MapS52ToAppealDecisionRow(db);
                        pdfBytes = _s52.BuildNotice(row, ctx);
                        pdfFileName = $"{roll.ShortCode}_S52_PREVIEW.pdf";

                        recipientName = FirstNonEmpty(db.Addr1, "Property Owner");
                        recipientEmail = db.Email ?? "";
                        addressLine = BuildAddrLine(db.Addr2, db.Addr3, db.Addr4, db.Addr5);
                        break;
                    }

                case NoticeKind.S53:
                    {
                        var preferMulti = isSplitPdf;
                        var db = await _previewDb.S53PreviewDbDataAsync(roll.RollId, preferMulti, ct);
                       

                        sampleObjectionNo = db.ObjectionNo ?? "";
                        valuationKey = db.ValuationKey ?? "";
                        samplePropertyDesc = db.PropertyDesc ?? ""; // ✅ NEW

                        var row = MapS53ToRow(db, settings, rollName);
                        pdfBytes = _s53.BuildNoticePdf(row, DateOnly.FromDateTime(settings.LetterDate));
                        pdfFileName = $"{roll.ShortCode}_S53_PREVIEW.pdf";

                        recipientName = FirstNonEmpty(db.Addr1, "Property Owner");
                        recipientEmail = db.Email ?? "";
                        addressLine = BuildAddrLine(db.Addr2, db.Addr3, db.Addr4, db.Addr5);

                        sampleAppealNo = "";
                        break;
                    }

                case NoticeKind.DJ:
                    {
                        var db = await _previewDb.DJPreviewDbDataAsync(roll.RollId, ct);

                        sampleObjectionNo = db.ObjectionNo ?? "";
                        valuationKey = ""; // add later if in source
                        samplePropertyDesc = db.PropertyDesc ?? ""; // ✅ NEW

                        var ctx = new DearJonnyPdfContext
                        {
                            HeaderImagePath = headerPath,
                            LetterDate = settings.LetterDate
                        };

                        var data = new DearJonnyPdfData
                        {
                            RollName = roll.Name ?? "",
                            ObjectionNo = db.ObjectionNo ?? "",
                            PropertyDescription = db.PropertyDesc ?? "",
                            Addr1 = db.Addr1 ?? "",
                            Addr2 = db.Addr2 ?? "",
                            Addr3 = db.Addr3 ?? "",
                            Addr4 = db.Addr4 ?? "",
                            Addr5 = db.Addr5 ?? "",
                            ValuationKey = db.ValuationKey ?? "",
                        };

                        pdfBytes = _dj.BuildNotice(data, ctx);
                        pdfFileName = $"{roll.ShortCode}_DJ_PREVIEW.pdf";

                        recipientName = FirstNonEmpty(db.Addr1, "Property Owner");
                        recipientEmail = db.Email ?? "";
                        addressLine = BuildAddrLine(db.Addr2, db.Addr3, db.Addr4, db.Addr5);

                        sampleAppealNo = "";
                        break;
                    }

                case NoticeKind.IN:
                    {
                        var isOmission = (variant == PreviewVariant.InvalidOmission);

                        var db = await _previewDb.InvalidPreviewDbDataAsync(roll.RollId, isOmission, ct);

                        sampleObjectionNo = db.ObjectionNo ?? "";
                        valuationKey = ""; // add later if in source
                        samplePropertyDesc = db.PropertyDesc ?? ""; // ✅ NEW

                        var ctx = new InvalidNoticePdfContext
                        {
                            HeaderImagePath = headerPath,
                            LetterDate = settings.LetterDate
                        };

                        var data = new InvalidNoticePdfData
                        {
                            Kind = isOmission ? Invalidity.InvalidNoticeKind.InvalidOmission : Invalidity.InvalidNoticeKind.InvalidObjection,
                            ObjectionNo = db.ObjectionNo ?? "",
                            PropertyDescription = db.PropertyDesc ?? "",
                            RecipientName = db.Addr1 ?? "",
                            RecipientAddress = BuildAddrLine(db.Addr1, db.Addr2, db.Addr3, db.Addr4, db.Addr5),
                            ValuationKey=db.ValuationKey ??"",
                        };

                        pdfBytes = _inv.BuildNotice(data, ctx);
                        pdfFileName = $"{roll.ShortCode}_IN_PREVIEW.pdf";

                        recipientName = FirstNonEmpty(db.Addr1, "Sir/Madam");
                        recipientEmail = db.Email ?? "";
                        addressLine = BuildAddrLine(db.Addr2, db.Addr3, db.Addr4, db.Addr5);

                        sampleAppealNo = "";
                        break;
                    }

                default:
                    throw new NotSupportedException($"Preview not implemented for notice {settings.Notice}.");
            }

            // =========================
            // 2) Email preview (real DB context)
            // =========================
            var emailReq = BuildEmailReqFromReal(
                settings,
                roll,
                recipientName,
                recipientEmail,
                sampleObjectionNo,
                sampleAppealNo,
                samplePropertyDesc,  // ✅ NEW
                valuationKey,
                variant,
                isEmailMulti);

            var (subject, body) = _email.Build(emailReq);

            return new NoticePreviewResult
            {
                SettingsId = settings.Id,
                RollId = roll.RollId,
                RollShortCode = roll.ShortCode,
                RollName = roll.Name,
                Notice = settings.Notice,
                Mode = settings.Mode,
                Version = settings.Version,

                RecipientName = recipientName,
                RecipientEmail = recipientEmail,
                AddressLine = addressLine,

                SampleObjectionNo = sampleObjectionNo,
                SampleAppealNo = sampleAppealNo,

                EmailSubject = subject,
                EmailBodyHtml = body,

                PdfBytes = pdfBytes,
                PdfFileName = pdfFileName
            };
        }

        // =========================
        // MAPPERS / HELPERS
        // =========================

        private static Section49PdfData MapS49ToPdf(S49PreviewDbData db, bool forceFourRows)
        {
            static string Money(decimal? v) =>
                v.HasValue ? "R " + v.Value.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ") : "";

            static string Num(decimal? v) =>
                v.HasValue ? v.Value.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ") : "";

            static string? FirstNonEmpty(params string?[] values)
            {
                foreach (var v in values)
                    if (!string.IsNullOrWhiteSpace(v))
                        return v;
                return null;
            }

            static string? GetStr(RowMap r, params string[] keys)
            {
                foreach (var k in keys)
                {
                    var v = r.Str(k);
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
                return null;
            }

            static decimal? GetDec(RowMap r, params string[] keys)
            {
                foreach (var k in keys)
                {
                    var v = r.Dec(k);
                    if (v.HasValue) return v;
                }
                return null;
            }

            // 1) Source rows
            var src = (db.RollRows ?? new List<RowMap>())
                .Where(r => !string.IsNullOrWhiteSpace(GetStr(r, "PREMISEID", "PremiseId", "PREMISE_ID")))
                .ToList();

            // fallback to single-row mapping if RollRows not populated
            if (src.Count == 0)
            {
                var rowsFallback = new List<Section49PropertyRow>
        {
            new Section49PropertyRow
            {
                Category = db.CatDesc ?? "",
                MarketValue = Money(db.MarketValue),
                Extent = Num(db.RateableArea),
                Remarks = db.Reason ?? ""
            }
        };

                return new Section49PdfData
                {
                    Addr1 = db.Addr1 ?? "",
                    Addr2 = db.Addr2 ?? "",
                    Addr3 = db.Addr3 ?? "",
                    Addr4 = db.Addr4 ?? "",
                    Addr5 = db.Addr5 ?? "",
                    PropertyDesc = db.PropertyDesc ?? "",
                    PhysicalAddress = db.LisStreetAddress ?? "",
                    ValuationKey = db.ValuationKey ?? "",
                    ForceFourRows = forceFourRows,
                    PropertyRows = rowsFallback
                };
            }

            // ✅ NEW: populate PropertyDesc + PhysicalAddress from header row (so PDF shows them)
            var headerRow = src[0];

            var propertyDesc = FirstNonEmpty(
                GetStr(headerRow, "PropertyDesc", "Property_Desc", "PROPERTYDESC"),
                db.PropertyDesc,
                ""
            ) ?? "";

            var physicalAddr = FirstNonEmpty(
                GetStr(headerRow, "LisStreetAddress", "LIS_STREET_ADDRESS", "PREMISE_ADDRESS", "PhysicalAddress"),
                db.LisStreetAddress,
                db.PremiseAddress,
                ""
            ) ?? "";

            // 2) Build PDF property rows
            if (forceFourRows)
            {
                if (src.Count > 4) src = src.Take(4).ToList();
            }
            else
            {
                // ✅ Optional safety: ensure single mode prints ONE row only
                if (src.Count > 1) src = src.Take(1).ToList();
            }

            var rows = src.Select(r =>
            {
                var cat = FirstNonEmpty(
                    GetStr(r, "CatDesc", "Category", "Old_Category", "PROPERTY_CATEGORY"),
                    db.CatDesc,
                    ""
                );

                var mv = GetDec(r, "MarketValue", "Market_Value", "Old_Market_Value", "GV_Market_Value") ?? db.MarketValue;
                var ext = GetDec(r, "RateableArea", "Rateable_Area", "Extent", "RATEABLEAREA") ?? db.RateableArea;

                var splitMark = FirstNonEmpty(GetStr(r, "ValuationSplitIndicator"), "");
                var reason = FirstNonEmpty(GetStr(r, "Reason", "Remarks"), db.Reason, "");

                var remarks = string.IsNullOrWhiteSpace(splitMark)
                    ? reason ?? ""
                    : $"{splitMark} {reason}".Trim();

                return new Section49PropertyRow
                {
                    Category = cat ?? "",
                    MarketValue = Money(mv),
                    Extent = Num(ext),
                    Remarks = remarks ?? ""
                };
            }).ToList();

            // 3) If ForceFourRows then pad up to 4 (blank lines)
            if (forceFourRows)
            {
                while (rows.Count < 4)
                {
                    rows.Add(new Section49PropertyRow
                    {
                        Category = "",
                        MarketValue = "",
                        Extent = "",
                        Remarks = ""
                    });
                }
            }

            return new Section49PdfData
            {
                Addr1 = db.Addr1 ?? "",
                Addr2 = db.Addr2 ?? "",
                Addr3 = db.Addr3 ?? "",
                Addr4 = db.Addr4 ?? "",
                Addr5 = db.Addr5 ?? "",

                // ✅ NEW: now these will show on the PDF
                PropertyDesc = propertyDesc,
                PhysicalAddress = physicalAddr,

                ValuationKey = db.ValuationKey ?? "",
                ForceFourRows = forceFourRows,
                PropertyRows = rows
            };
        }

        private static Section51NoticeData MapS51ToPdf(S51PreviewDbData db, string rollName)
        {
            string Money(decimal? v) => v.HasValue ? v.Value.ToString(CultureInfo.InvariantCulture) : "";

            return new Section51NoticeData
            {
                RollName = rollName,
                ObjectionNo = db.ObjectionNo ?? "",
                Section51Pin = db.Section51Pin,


                Addr1 = db.Addr1 ?? "",
                Addr2 = db.Addr2 ?? "",
                Addr3 = db.Addr3 ?? "",
                Addr4 = db.Addr4 ?? "",
                Addr5 = db.Addr5 ?? "",
                ValuationKey = db.valuationKey,
                PropertyDesc = db.PropertyDesc ?? "",
                IsMulti = string.Equals(db.PropertyType?.Trim(), "Multi", StringComparison.OrdinalIgnoreCase),


                Section6 = new Section6Row
                {
                    Old_Category = db.OldCategory,
                    Old2_Category = db.Old2Category,
                    Old3_Category = db.Old3Category,

                    Old_Extent = db.OldExtent?.ToString(CultureInfo.InvariantCulture),
                    Old2_Extent = db.Old2Extent?.ToString(CultureInfo.InvariantCulture),
                    Old3_Extent = db.Old3Extent?.ToString(CultureInfo.InvariantCulture),
                    Old_Market_Value = db.OldMarketValue?.ToString(),
                    Old2_Market_Value = db.Old2MarketValue?.ToString(),
                    Old3_Market_Value = db.Old3MarketValue?.ToString(),

                    New_Market_Value = db.NewMarketValue?.ToString(),
                    New2_Market_Value = db.New2MarketValue?.ToString(),
                    New3_Market_Value = db.New3MarketValue?.ToString(),

                    New_Category = db.NewCategory,
                    New2_Category = db.New2Category,
                    New3_Category = db.New3Category,

                    New_Extent = db.NewExtent?.ToString(CultureInfo.InvariantCulture),
                    New2_Extent = db.New2Extent?.ToString(CultureInfo.InvariantCulture),
                    New3_Extent = db.New3Extent?.ToString(CultureInfo.InvariantCulture),



                }
            };
        }

        private static AppealDecisionRow MapS52ToAppealDecisionRow(S52PreviewDbData db)
        {
            return new AppealDecisionRow
            {
                A_UserID = db.AUserId,
                ADDR1 = db.Addr1,
                ADDR2 = db.Addr2,
                ADDR3 = db.Addr3,
                ADDR4 = db.Addr4,
                ADDR5 = db.Addr5,
                Email = db.Email,
                Property_desc = db.PropertyDesc,
                Town = db.Town,
                ERF = db.Erf,
                PTN = db.Ptn,
                RE = db.Re,
                Objection_No = db.ObjectionNo,
                Appeal_No = db.AppealNo,
                valuation_Key = db.ValuationKey,

                App_Category = db.AppCategory,
                App_Category2 = db.AppCategory2,
                App_Category3 = db.AppCategory3,

                App_Extent = db.AppExtent,
                App_Extent2 = db.AppExtent2,
                App_Extent3 = db.AppExtent3,

                App_Market_Value = db.AppMarketValue,
                App_Market_Value2 = db.AppMarketValue2,
                App_Market_Value3 = db.AppMarketValue3
            };
        }
        private static Section53MvdRow MapS53ToRow(S53PreviewDbData db, NoticeSettings settings, string rollName)
        {
            return new Section53MvdRow
            {
                ObjectionNo = db.ObjectionNo,
                PropertyDesc = db.PropertyDesc,
                Addr1 = db.Addr1,
                Addr2 = db.Addr2,
                Addr3 = db.Addr3,
                Addr4 = db.Addr4,
                Addr5 = db.Addr5,

                ValuationKey = db.ValuationKey,
                Section52Review = db.Section52Review,

                AppealCloseDate = (settings.AppealCloseDate ?? db.AppealCloseDate) ?? settings.LetterDate.AddDays(45),

                Gv_Category = db.GvCategory ?? "",
                Gv_Category2 = db.GvCategory2 ?? "",
                Gv_Category3 = db.GvCategory3 ?? "",

                Gv_Market_Value = db.GvMarketValue ?? "",
                Gv_Market_Value2 = db.GvMarketValue2 ?? "",
                Gv_Market_Value3 = db.GvMarketValue3 ?? "",

                Gv_Extent = db.GvExtent ?? "",
                Gv_Extent2 = db.GvExtent2 ?? "",
                Gv_Extent3 = db.GvExtent3 ?? "",

                Mvd_Category = db.MvdCategory ?? "",
                Mvd_Category2 = db.MvdCategory2 ?? "",
                Mvd_Category3 = db.MvdCategory3 ?? "",

                Mvd_Market_Value = db.MvdMarketValue ?? "",
                Mvd_Market_Value2 = db.MvdMarketValue2 ?? "",
                Mvd_Market_Value3 = db.MvdMarketValue3 ?? "",

                Mvd_Extent = db.MvdExtent ?? "",
                Mvd_Extent2 = db.MvdExtent2 ?? "",
                Mvd_Extent3 = db.MvdExtent3 ?? "",
                RollName = rollName,
            };
        }
        private static NoticeEmailRequest BuildEmailReqFromReal(
      NoticeSettings s,
      RollRegistry roll,
      string recipientName,
      string recipientEmail,
      string objectionNo,
      string appealNo,
      string propertyDesc,
      string valuationKey,
      PreviewVariant variant,
      bool isMulti)
        {
            var req = new NoticeEmailRequest
            {
                Notice = s.Notice,
                RollShortCode = roll.ShortCode ?? "",
                RollName = roll.Name ?? "",
                RecipientName = recipientName ?? "",
                RecipientEmail = recipientEmail ?? "",
                IsMulti = isMulti,
                Items = new List<NoticeEmailPropertyItem>
        {
            new NoticeEmailPropertyItem
            {
                PropertyDesc = BuildItemPropertyDesc(propertyDesc, valuationKey),
                ObjectionNo = objectionNo ?? "",
                AppealNo = appealNo ?? ""
            }
        }
            };

            if (s.Notice == NoticeKind.S52)
                req.IsSection52Review = (variant == PreviewVariant.S52ReviewDecision);

            if (s.Notice == NoticeKind.IN)
            {
                req.InvalidKind =
                    (variant == PreviewVariant.InvalidOmission)
                        ? GV23_Notice.Services.Email.InvalidNoticeKind.InvalidOmission
                        : GV23_Notice.Services.Email.InvalidNoticeKind.InvalidObjection;
            }

            return req;
        }
        private static string FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";

        private static string BuildAddrLine(params string?[] parts)
            => string.Join(", ", parts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));

        private static string BuildItemPropertyDesc(string? propertyDesc, string? valuationKey)
        {
            var p = (propertyDesc ?? "").Trim();
            var vk = (valuationKey ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(p) &&
                !string.IsNullOrWhiteSpace(vk) &&
                !p.Contains(vk, StringComparison.OrdinalIgnoreCase))
                return $"{p} (Valuation Key: {vk})";

            if (!string.IsNullOrWhiteSpace(p))
                return p;

            if (!string.IsNullOrWhiteSpace(vk))
                return $"Valuation Key: {vk}";

            return "";
        }
    }
}