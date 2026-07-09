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
using GV23_Notice.Services.ThirdPartyApplications;
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
        private readonly IThirdPartyAppealFormalNoticePdfService _tpaPdf;

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
            IThirdPartyAppealFormalNoticePdfService tpaPdf,
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
            _tpaPdf = tpaPdf;
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

            var isTpa = settings.Notice == NoticeKind.TPA;

            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.RollId == settings.RollId, ct);

            /*
             * Normal notices still require RollRegistry.
             * TPA / Application to the Valuation Appeal does not require the user to select a roll.
             */
            if (roll is null && !isTpa)
                throw new InvalidOperationException("RollRegistry not found.");

            var rollName = roll?.Name ?? settings.RollName ?? "Valuation Roll";
            var rollShortCode = roll?.ShortCode ?? "GV23";
            var rollId = roll?.RollId ?? settings.RollId;

            var headerPath = Path.Combine(_env.WebRootPath, "Images", "Obj_Header.PNG");

            // Mode flags
            var isSplitPdf = mode == PreviewMode.SplitPdf;
            var isEmailMulti = mode == PreviewMode.EmailMulti;
            var rollNames = settings.RollName ?? rollName;

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
            string samplePropertyDesc = "";

            switch (settings.Notice)
            {
                case NoticeKind.S49:
                    {
                        var db = await _previewDb.S49PreviewDbDataAsync(rollId, split: isSplitPdf, ct);

                        valuationKey = db.ValuationKey ?? "";
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
                            RollHeaderText = rollNames
                        };

                        var data = MapS49ToPdf(db, forceFourRows: isSplitPdf);

                        pdfBytes = _s49.BuildNotice(data, ctx);
                        pdfFileName = $"{rollShortCode}_S49_PREVIEW.pdf";

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
                        var db = await _previewDb.S51PreviewDbDataAsync(rollId, preferMulti, ct);

                        sampleObjectionNo = db.ObjectionNo ?? "";
                        valuationKey = "";
                        samplePropertyDesc = db.PropertyDesc ?? "";

                        var ctx = new Section51NoticeContext
                        {
                            HeaderImagePath = headerPath,
                            LetterDate = settings.LetterDate,
                            SubmissionsCloseDate = settings.EvidenceCloseDate ?? settings.LetterDate.AddDays(30),
                            PortalUrl = string.IsNullOrWhiteSpace(settings.PortalUrl)
                                ? "https://objections.joburg.org.za/"
                                : settings.PortalUrl,
                            SignOffName = "S. Faiaz",
                            SignOffTitle = "Municipal Valuer"
                        };

                        var data = MapS51ToPdf(db, rollName);
                        pdfBytes = _s51.BuildNotice(data, ctx);
                        pdfFileName = $"{rollShortCode}_S51_PREVIEW.pdf";

                        recipientName = FirstNonEmpty(db.Addr1, "Property Owner");
                        recipientEmail = db.Email ?? "";
                        addressLine = BuildAddrLine(db.Addr2, db.Addr3, db.Addr4, db.Addr5);
                        sampleAppealNo = "";
                        break;
                    }

                case NoticeKind.S52:
                    {
                        var isReview = variant == PreviewVariant.S52ReviewDecision;

                        var db = await _previewDb.S52PreviewDbDataAsync(
                            rollId,
                            appealNo ?? "",
                            isReview,
                            ct);

                        sampleAppealNo = db.AppealNo ?? appealNo ?? "";
                        sampleObjectionNo = db.ObjectionNo ?? "";
                        valuationKey = db.ValuationKey ?? "";
                        samplePropertyDesc = db.PropertyDesc ?? "";

                        var ctx = new Section52PdfContext
                        {
                            HeaderImagePath = headerPath,
                            LetterDate = DateOnly.FromDateTime(settings.LetterDate)
                        };

                        var row = MapS52ToAppealDecisionRow(db);

                        pdfBytes = _s52.BuildNotice(row, ctx);
                        pdfFileName = $"{rollShortCode}_S52_PREVIEW.pdf";

                        recipientName = FirstNonEmpty(db.Addr1, "Property Owner");
                        recipientEmail = db.Email ?? "";
                        addressLine = BuildAddrLine(db.Addr2, db.Addr3, db.Addr4, db.Addr5);
                        break;
                    }

                case NoticeKind.S53:
                    {
                        var preferMulti = isSplitPdf;
                        var db = await _previewDb.S53PreviewDbDataAsync(rollId, preferMulti, ct);

                        sampleObjectionNo = db.ObjectionNo ?? "";
                        valuationKey = db.ValuationKey ?? "";
                        samplePropertyDesc = db.PropertyDesc ?? "";

                        var row = MapS53ToRow(
                            db,
                            settings,
                            rollName,
                            isRevisedMvd: false);

                        pdfBytes = _s53.BuildNoticePdf(row, DateOnly.FromDateTime(settings.LetterDate));
                        pdfFileName = $"{rollShortCode}_S53_PREVIEW.pdf";

                        recipientName = FirstNonEmpty(db.Addr1, "Property Owner");
                        recipientEmail = db.Email ?? "";
                        addressLine = BuildAddrLine(db.Addr2, db.Addr3, db.Addr4, db.Addr5);

                        sampleAppealNo = "";
                        break;
                    }

                case NoticeKind.S53Rev:
                    {
                        var preferMulti = isSplitPdf;
                        var db = await _previewDb.S53RevPreviewDbDataAsync(rollId, preferMulti, ct);

                        sampleObjectionNo = db.ObjectionNo ?? "";
                        valuationKey = db.ValuationKey ?? "";
                        samplePropertyDesc = db.PropertyDesc ?? "";

                        var row = MapS53ToRow(
                            db,
                            settings,
                            rollName,
                            isRevisedMvd: true);

                        pdfBytes = _s53.BuildNoticePdf(row, DateOnly.FromDateTime(settings.LetterDate));
                        pdfFileName = $"{rollShortCode}_S53_REVISED_MVD_PREVIEW.pdf";

                        recipientName = FirstNonEmpty(db.Addr1, "Property Owner");
                        recipientEmail = db.Email ?? "";
                        addressLine = BuildAddrLine(db.Addr2, db.Addr3, db.Addr4, db.Addr5);

                        sampleAppealNo = "";
                        break;
                    }

                case NoticeKind.DJ:
                    {
                        var db = await _previewDb.DJPreviewDbDataAsync(rollId, ct);

                        sampleObjectionNo = db.ObjectionNo ?? "";
                        valuationKey = db.ValuationKey ?? "";
                        samplePropertyDesc = db.PropertyDesc ?? "";

                        var ctx = new DearJonnyPdfContext
                        {
                            HeaderImagePath = headerPath,
                            LetterDate = settings.LetterDate
                        };

                        var data = new DearJonnyPdfData
                        {
                            RollName = rollName,
                            ObjectionNo = db.ObjectionNo ?? "",
                            PropertyDescription = db.PropertyDesc ?? "",
                            Addr1 = db.Addr1 ?? "",
                            Addr2 = db.Addr2 ?? "",
                            Addr3 = db.Addr3 ?? "",
                            Addr4 = db.Addr4 ?? "",
                            Addr5 = db.Addr5 ?? "",
                            ValuationKey = db.ValuationKey ?? ""
                        };

                        pdfBytes = _dj.BuildNotice(data, ctx);
                        pdfFileName = $"{rollShortCode}_DJ_PREVIEW.pdf";

                        recipientName = FirstNonEmpty(db.Addr1, "Property Owner");
                        recipientEmail = db.Email ?? "";
                        addressLine = BuildAddrLine(db.Addr2, db.Addr3, db.Addr4, db.Addr5);

                        sampleAppealNo = "";
                        break;
                    }

                case NoticeKind.IN:
                    {
                        var isOmission = variant == PreviewVariant.InvalidOmission;

                        var db = await _previewDb.InvalidPreviewDbDataAsync(rollId, isOmission, ct);

                        sampleObjectionNo = db.ObjectionNo ?? "";
                        valuationKey = db.ValuationKey ?? "";
                        samplePropertyDesc = db.PropertyDesc ?? "";

                        var ctx = new InvalidNoticePdfContext
                        {
                            HeaderImagePath = headerPath,
                            LetterDate = settings.LetterDate
                        };

                        var data = new InvalidNoticePdfData
                        {
                            Kind = isOmission
                                ? Invalidity.InvalidNoticeKind.InvalidOmission
                                : Invalidity.InvalidNoticeKind.InvalidObjection,
                            ObjectionNo = db.ObjectionNo ?? "",
                            PropertyDescription = db.PropertyDesc ?? "",
                            RecipientName = db.Addr1 ?? "",
                            RecipientAddress = BuildAddrLine(db.Addr1, db.Addr2, db.Addr3, db.Addr4, db.Addr5),
                            ValuationKey = db.ValuationKey ?? ""
                        };

                        pdfBytes = _inv.BuildNotice(data, ctx);
                        pdfFileName = $"{rollShortCode}_IN_PREVIEW.pdf";

                        recipientName = FirstNonEmpty(db.Addr1, "Sir/Madam");
                        recipientEmail = db.Email ?? "";
                        addressLine = BuildAddrLine(db.Addr2, db.Addr3, db.Addr4, db.Addr5);

                        sampleAppealNo = "";
                        break;
                    }

                case NoticeKind.TPA:
                    {
                        /*
                         * Application to the Valuation Appeal / Third-Party Appeal Application.
                         * No batches.
                         * No user-selected roll required.
                         * Preview comes directly from ThirdPartyAppealApplicationNotices.
                         */

                        var preferMulti = isSplitPdf || isEmailMulti;

                        var db = await LoadThirdPartyAppealPreviewRowAsync(
                            preferMulti,
                            ct);

                        if (db == null)
                            throw new InvalidOperationException("No Third-Party Appeal Application records found for preview.");

                        sampleAppealNo = db.AppealNo ?? "";
                        sampleObjectionNo = db.ObjectionNo ?? "";
                        valuationKey = db.ValuationKey ?? "";
                        samplePropertyDesc = db.PropertyDescription ?? "";

                        /*
                         * Use your TPA PDF builder.
                         *
                         * This assumes you added:
                         * private readonly IThirdPartyAppealFormalNoticePdfService _tpaPdf;
                         *
                         * and the method:
                         * byte[] BuildPdf(NoticeSettings settings, ThirdPartyAppealApplicationNotice notice)
                         */
                        pdfBytes = _tpaPdf.BuildPdf(settings, db.Entity);

                        pdfFileName = $"{rollShortCode}_TPA_PREVIEW_{sampleAppealNo}.pdf";

                        recipientName = FirstNonEmpty(db.OwnerName, "Property Owner");
                        recipientEmail = db.OwnerEmail ?? "";
                        addressLine = BuildAddrLine(
                            db.OwnerAddress1,
                            db.OwnerAddress2,
                            db.OwnerAddress3,
                            db.OwnerAddress4,
                            db.OwnerAddress5);

                        break;
                    }

                default:
                    throw new NotSupportedException($"Preview not implemented for notice {settings.Notice}.");
            }

            // =========================
            // 2) Email preview
            // =========================
            var emailReq = BuildEmailReqFromReal(
                settings,
                roll,
                recipientName,
                recipientEmail,
                sampleObjectionNo,
                sampleAppealNo,
                samplePropertyDesc,
                valuationKey,
                variant,
                isEmailMulti);

            var (subject, body) = _email.Build(emailReq);

            return new NoticePreviewResult
            {
                SettingsId = settings.Id,
                RollId = rollId,
                RollShortCode = rollShortCode,
                RollName = rollName,
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
        private async Task<ThirdPartyAppealPreviewLite?> LoadThirdPartyAppealPreviewRowAsync(
    bool preferMulti,
    CancellationToken ct)
        {
            var query = _db.ThirdPartyAppealApplicationNotices
                .AsNoTracking()
                .Where(x => !string.IsNullOrWhiteSpace(x.Appeal_No));

            if (preferMulti)
            {
                query = query.Where(x =>
                    x.Property_Type == "Multi" ||
                    x.Property_Type == "Multipurpose"
                   );
            }
            else
            {
                query = query.Where(x =>
                    x.Property_Type != "Multi" &&
                    x.Property_Type != "Multipurpose");
            }

            return await query
                .OrderBy(x => x.Appeal_No)
                .Select(x => new ThirdPartyAppealPreviewLite
                {
                    Entity = x,

                    AppealNo = x.Appeal_No,
                    ObjectionNo = x.Objection_No,
                    PremiseId = x.Premise_ID,
                    ValuationKey = x.Valuation_Key,
                    PropertyType = x.Property_Type,
                    PropertyDescription = x.Property_Description,

                    OwnerName = x.OwnerName,
                    OwnerEmail = x.OwnerEmail,
                    OwnerAddress1 = x.OwnerAddress1,
                    OwnerAddress2 = x.OwnerAddress2,
                    OwnerAddress3 = x.OwnerAddress3,
                    OwnerAddress4 = x.OwnerAddress4,
                    OwnerAddress5 = x.OwnerAddress5
                })
                .FirstOrDefaultAsync(ct);
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
        private static Section53MvdRow MapS53ToRow(
      S53PreviewDbData db,
      NoticeSettings settings,
      string rollName,
      bool isRevisedMvd)
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

                // IMPORTANT:
                // Revised MVD appeal close date comes from Step 1 Date Configuration.
                AppealCloseDate = settings.AppealCloseDate
                                  ?? settings.LetterDate.AddDays(45),

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

                WEFMVD = db.WEFMVD ?? "",

                RollName = rollName,
                IsRevisedMvd = isRevisedMvd
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
        private sealed class ThirdPartyAppealPreviewLite
        {
            public ThirdPartyAppealApplicationNotice Entity { get; set; } = default!;

            public string? AppealNo { get; set; }
            public string? ObjectionNo { get; set; }
            public string? PremiseId { get; set; }
            public string? ValuationKey { get; set; }
            public string? PropertyType { get; set; }
            public string? PropertyDescription { get; set; }

            public string? OwnerName { get; set; }
            public string? OwnerEmail { get; set; }
            public string? OwnerAddress1 { get; set; }
            public string? OwnerAddress2 { get; set; }
            public string? OwnerAddress3 { get; set; }
            public string? OwnerAddress4 { get; set; }
            public string? OwnerAddress5 { get; set; }
        }
    }
}