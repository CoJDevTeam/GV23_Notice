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
using Microsoft.EntityFrameworkCore;
using InvalidNoticeKind = GV23_Notice.Services.Notices.Invalidity.InvalidNoticeKind;

namespace GV23_Notice.Services.Notices
{
    public sealed class NoticePreviewService : INoticePreviewService
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly IDummyPreviewDataFactory _dummy;
        private readonly INoticeEmailTemplateService _email;

        private readonly ISection49PdfBuilder _s49;
        private readonly ISection51PdfBuilder _s51;
        private readonly ISection52PdfService _s52;
        private readonly ISection53PdfService _s53;
        private readonly IDearJonnyPdfService _dj;
        private readonly IInvalidNoticePdfService _inv;
        private readonly ISection78PdfBuilder _s78;

        public NoticePreviewService(
            AppDbContext db,
            IWebHostEnvironment env,
            IDummyPreviewDataFactory dummy,
            ISection49PdfBuilder s49,
            ISection51PdfBuilder s51,
            ISection52PdfService s52,
            ISection53PdfService s53,
            IDearJonnyPdfService dj,
            IInvalidNoticePdfService inv,
            ISection78PdfBuilder s78,
            INoticeEmailTemplateService email)
        {
            _db = db;
            _env = env;
            _dummy = dummy;

            _s49 = s49;
            _s51 = s51;
            _s52 = s52;
            _s53 = s53;
            _dj = dj;
            _inv = inv;
            _s78 = s78;
            _email = email;
        }

       
        private static NoticeEmailRequest BuildEmailReq(
            NoticeSettings s,
            RollRegistry roll,
            DummyRecipient rec,
            DummyProperty prop,
            string objectionNo,
            string appealNo,
            PreviewVariant variant,
            bool isMulti)
        {
            var req = new NoticeEmailRequest
            {
                Notice = s.Notice,
                RollShortCode = roll.ShortCode ?? "",
                RollName = roll.Name ?? "",
                RecipientName = rec.Name ?? "",
                RecipientEmail = rec.Email ?? "",
                IsMulti = isMulti,
                Items = BuildItems(prop, objectionNo, appealNo, isMulti)
            };

            if (s.Notice == NoticeKind.S49)
            {
                var start = s.ObjectionStartDate ?? s.LetterDate;
                var end = s.ObjectionEndDate ?? s.LetterDate.AddDays(30);

                req.InspectionStart = DateOnly.FromDateTime(start);
                req.InspectionEnd = DateOnly.FromDateTime(end);
                req.ExtendedEnd = s.ExtensionDate.HasValue ? DateOnly.FromDateTime(s.ExtensionDate.Value) : null;
                req.FinancialYearsText = s.FinancialYearsText;
            }

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

            static List<NoticeEmailPropertyItem> BuildItems(DummyProperty prop, string objectionNo, string appealNo, bool isMulti)
            {
                if (!isMulti)
                {
                    return new List<NoticeEmailPropertyItem>
                    {
                        new NoticeEmailPropertyItem
                        {
                            PropertyDesc = prop.PropertyDescription ?? "PORTION 2 ERF 201 ROSEBANK",
                            ObjectionNo = objectionNo,
                            AppealNo = appealNo
                        }
                    };
                }

                return new List<NoticeEmailPropertyItem>
                {
                    new NoticeEmailPropertyItem { PropertyDesc = "PORTION 2 ERF 201 ROSEBANK", ObjectionNo = objectionNo, AppealNo = appealNo },
                    new NoticeEmailPropertyItem { PropertyDesc = "ERF 88 PARKTOWN", ObjectionNo = $"{objectionNo}-2", AppealNo = $"{appealNo}-2" },
                    new NoticeEmailPropertyItem { PropertyDesc = "ERF 15 AUCKLAND PARK", ObjectionNo = $"{objectionNo}-3", AppealNo = $"{appealNo}-3" }
                };
            }
        }

        // =========================
        // EXISTING SINGLE BUILDERS
        // =========================

        private byte[] BuildS49(NoticeSettings s, string rollShortCode, string headerPath, DummyRecipient rec, string objectionNo)
        {
            var preview = new Section49PreviewData
            {
                HeaderImagePath = headerPath,
                SignaturePath = s.SignaturePath,
                LetterDate = s.LetterDate,
                InspectionStartDate = s.ObjectionStartDate ?? s.LetterDate,
                InspectionEndDate = s.ObjectionEndDate ?? s.LetterDate.AddDays(30),
                ExtendedEndDate = s.ExtensionDate,
                FinancialYearsText = s.FinancialYearsText ?? $"{new DateTime(DateTime.Today.Year, 7, 1):dd MMMM yyyy} – {new DateTime(DateTime.Today.Year + 1, 6, 30):dd MMMM yyyy}",

                RollHeaderText = $"{rollShortCode} ROLL"
            };

            return _s49.BuildPreview(preview);
        }

        private byte[] BuildS51(NoticeSettings s, string headerPath, DummyRecipient rec, string objectionNo, DummyProperty prop)
        {
            var preview = new Section51PreviewData
            {
                HeaderImagePath = headerPath,
                LetterDate = s.LetterDate,
                SubmissionsCloseDate = (s.EvidenceCloseDate ?? s.LetterDate.AddDays(30)),
                PortalUrl = string.IsNullOrWhiteSpace(s.PortalUrl) ? "https://objections.joburg.org.za/" : s.PortalUrl,
                RollName = string.IsNullOrWhiteSpace(s.RollName) ? "General Valuation Roll 2023" : s.RollName,
                ObjectionNo = objectionNo,
                Section51Pin = "123456",
                PropertyFrom = "Printed"
            };

            return _s51.BuildPreview(preview);
        }

        private byte[] BuildS53(NoticeSettings s, DummyRecipient rec, string objectionNo)
        {
            var appealClose = s.AppealCloseDate?.Date ?? s.LetterDate.Date.AddDays(45);

            var row = new Section53MvdRow
            {
                ObjectionNo = objectionNo,
                PropertyDesc = "PORTION 2 ERF 201 ROSEBANK",
                Addr1 = rec.Name,
                Addr2 = rec.AddressLine,
                Addr3 = "Jorissen Street",
                Addr4 = "Braamfontein",
                Addr5 = "2001",
                AppealCloseDate = appealClose,
                Section52Review = "No",
                ValuationKey = "VAL-KEY-SAMPLE-001",
                Gv_Market_Value = "R 3 000 000",
                Gv_Extent = "1200",
                Gv_Category = "Residential",
                Mvd_Market_Value = "R 3 200 000",
                Mvd_Extent = "1200",
                Mvd_Category = "Residential"
            };

            return _s53.BuildPreviewPdf(row, DateOnly.FromDateTime(s.LetterDate));
        }

        private byte[] BuildDJ(NoticeSettings s, string headerPath, DummyRecipient rec, string objectionNo, string rollShortCode)
        {
            var preview = new DearJonnyPreviewData
            {
                HeaderImagePath = headerPath,
                LetterDate = s.LetterDate,
                RollName = rollShortCode,
                ObjectionNo = objectionNo,
                PropertyDescription = "PORTION 2 ERF 201 ROSEBANK",
                Addr1 = rec.Name,
                Addr2 = rec.AddressLine,
                Addr3 = "Jorissen Street",
                Addr4 = "Braamfontein",
                Addr5 = "2001"
            };

            return _dj.BuildPreview(preview);
        }

        private byte[] BuildS78(NoticeSettings s, string headerPath, DummyRecipient rec, DummyProperty prop)
        {
            var ctx = new Section78PdfContext { HeaderImagePath = headerPath };

            var open = s.ReviewOpenDate.HasValue ? DateOnly.FromDateTime(s.ReviewOpenDate.Value) : DateOnly.FromDateTime(s.LetterDate.AddDays(14));
            var close = s.ReviewCloseDate.HasValue ? DateOnly.FromDateTime(s.ReviewCloseDate.Value) : open.AddDays(42);

            var data = new Section78PreviewData
            {
                OwnerName = "BANK THE",
                PostalLine1 = "32 ROSEBANK",
                PostalLine2 = "ROSEBANK",
                PostalCode = "2196",
                LetterDate = DateOnly.FromDateTime(s.LetterDate),
                ReviewOpenDate = open,
                ReviewCloseDate = close,
                PropertyDescription = prop.PropertyDescription,
                PremiseId = prop.PremiseId,
                PropertyCategory = prop.Category,
                MarketValue = prop.MarketValue,
                Extent = prop.Extent,
                EffectiveDate = prop.EffectiveDate
            };

            return _s78.BuildPreview(data, ctx);
        }

        // =========================
        // ✅ MULTI DUMMY BUILDERS (SIGNATURES MATCH SINGLE BUILDERS)
        // =========================

        private byte[] BuildS49MultiDummy(NoticeSettings s, string rollShortCode, string headerPath, DummyRecipient rec, string objectionNo)
        {
            // Multi preview without changing models: keep same template, just tweak header text.
            var preview = new Section49PreviewData
            {
                HeaderImagePath = headerPath,
                SignaturePath = s.SignaturePath,
                LetterDate = s.LetterDate,
                InspectionStartDate = s.ObjectionStartDate ?? s.LetterDate,
                InspectionEndDate = s.ObjectionEndDate ?? s.LetterDate.AddDays(30),
                ExtendedEndDate = s.ExtensionDate,
                FinancialYearsText = s.FinancialYearsText ?? $"{new DateTime(DateTime.Today.Year, 7, 1):dd MMMM yyyy} – {new DateTime(DateTime.Today.Year + 1, 6, 30):dd MMMM yyyy}",

                // ✅ indicates multi in the preview visually
                RollHeaderText = $"{rollShortCode} ROLL (MULTI PREVIEW)"
            };

            return _s49.BuildPreview(preview);
        }

        private byte[] BuildS51MultiDummy(NoticeSettings s, string headerPath, DummyRecipient rec, string objectionNo, DummyProperty prop)
        {
            var preview = new Section51PreviewData
            {
                HeaderImagePath = headerPath,
                LetterDate = s.LetterDate,
                SubmissionsCloseDate = (s.EvidenceCloseDate ?? s.LetterDate.AddDays(30)),
                PortalUrl = string.IsNullOrWhiteSpace(s.PortalUrl) ? "https://objections.joburg.org.za/" : s.PortalUrl,
                RollName = string.IsNullOrWhiteSpace(s.RollName) ? "General Valuation Roll 2023" : s.RollName,

                // show multi ref
                ObjectionNo = $"{objectionNo}-MULTI",
                Section51Pin = "123456",
                PropertyFrom = "Printed"
            };

            // If your S51 PDF supports a property description field, you can set it here.
            // If it doesn't exist in your model, do NOT add it.
            // preview.PropertyDescription = "MULTI PROPERTY PREVIEW: ...";

            return _s51.BuildPreview(preview);
        }

        private byte[] BuildS53MultiDummy(NoticeSettings s, DummyRecipient rec, string objectionNo)
        {
            var appealClose = s.AppealCloseDate?.Date ?? s.LetterDate.Date.AddDays(45);

            var row = new Section53MvdRow
            {
                ObjectionNo = $"{objectionNo}-MULTI",
                PropertyDesc =
                    "MULTI PROPERTY PREVIEW:\n" +
                    "• PORTION 2 ERF 201 ROSEBANK\n" +
                    "• ERF 88 PARKTOWN\n" +
                    "• ERF 15 AUCKLAND PARK",

                Addr1 = rec.Name,
                Addr2 = rec.AddressLine,
                Addr3 = "Jorissen Street",
                Addr4 = "Braamfontein",
                Addr5 = "2001",

                AppealCloseDate = appealClose,
                Section52Review = "No",
                ValuationKey = "VAL-KEY-S53-MULTI",

                Gv_Market_Value = "R 3 000 000",
                Gv_Extent = "1200",
                Gv_Category = "Residential",

                Mvd_Market_Value = "R 3 200 000",
                Mvd_Extent = "1200",
                Mvd_Category = "Residential"
            };

            return _s53.BuildPreviewPdf(row, DateOnly.FromDateTime(s.LetterDate));
        }

        private byte[] BuildDJMultiDummy(NoticeSettings s, string headerPath, DummyRecipient rec, string objectionNo, string rollShortCode)
        {
            var preview = new DearJonnyPreviewData
            {
                HeaderImagePath = headerPath,
                LetterDate = s.LetterDate,
                RollName = rollShortCode,

                ObjectionNo = $"{objectionNo}-MULTI",
                PropertyDescription =
                    "MULTI PROPERTY PREVIEW:\n" +
                    "• OBJ-GV23-0001 - PORTION 2 ERF 201 ROSEBANK\n" +
                    "• OBJ-GV23-0002 - ERF 88 PARKTOWN\n" +
                    "• OBJ-GV23-0003 - ERF 15 AUCKLAND PARK",

                Addr1 = rec.Name,
                Addr2 = rec.AddressLine,
                Addr3 = "Jorissen Street",
                Addr4 = "Braamfontein",
                Addr5 = "2001"
            };

            return _dj.BuildPreview(preview);
        }

        private byte[] BuildS78MultiDummy(NoticeSettings s, string headerPath, DummyRecipient rec, DummyProperty prop)
        {
            var ctx = new Section78PdfContext { HeaderImagePath = headerPath };

            var open = s.ReviewOpenDate.HasValue
                ? DateOnly.FromDateTime(s.ReviewOpenDate.Value)
                : DateOnly.FromDateTime(s.LetterDate.AddDays(14));

            var close = s.ReviewCloseDate.HasValue
                ? DateOnly.FromDateTime(s.ReviewCloseDate.Value)
                : open.AddDays(42);

            var data = new Section78PreviewData
            {
                OwnerName = rec.Name ?? "BANK THE",
                PostalLine1 = rec.AddressLine ?? "32 ROSEBANK",
                PostalLine2 = "ROSEBANK",
                PostalCode = "2196",

                LetterDate = DateOnly.FromDateTime(s.LetterDate),
                ReviewOpenDate = open,
                ReviewCloseDate = close,

                PropertyDescription =
                    "MULTI PROPERTY PREVIEW:\n" +
                    "• PORTION 2 ERF 201 ROSEBANK\n" +
                    "• ERF 88 PARKTOWN\n" +
                    "• ERF 15 AUCKLAND PARK",

                PremiseId = "MULTI-PREVIEW",
                PropertyCategory = prop.Category ?? "Residential",
                MarketValue = prop.MarketValue,
                Extent = prop.Extent,
                EffectiveDate = prop.EffectiveDate
            };

            return _s78.BuildPreview(data, ctx);
        }

        // =========================
        // SELECTORS
        // =========================

        private byte[] BuildINSelector(
            NoticeSettings s,
            string headerPath,
            DummyRecipient rec,
            string objectionNo,
            string rollShortCode,
            PreviewVariant variant)
        {
            var kind = (variant == PreviewVariant.InvalidOmission)
                ? InvalidNoticeKind.InvalidOmission
                : InvalidNoticeKind.InvalidObjection;

            var preview = new InvalidNoticePreviewData
            {
                HeaderImagePath = headerPath,
                LetterDate = s.LetterDate,
                Kind = kind,
                ObjectionNo = objectionNo,
                PropertyDescription = "Sample Property",
                RecipientName = rec.Name,
                RecipientAddress = $"{rec.AddressLine}\nXXXX\nXXXX\nXXXX"
            };

            return _inv.BuildPreview(preview);
        }

        private byte[] BuildS52Selector(
            NoticeSettings s,
            DummyRecipient rec,
            string appealNo,
            PreviewVariant variant,
            bool isMulti)
        {
            var wantReview = (variant == PreviewVariant.S52ReviewDecision);
            var wantMulti = isMulti;

            if (wantReview && wantMulti)
                return BuildS52ReviewMultiDummy(s, rec, appealNo);

            if (wantReview && !wantMulti)
                return BuildS52ReviewSingleDummy(s, rec, appealNo);

            if (!wantReview && wantMulti)
                return BuildS52AppealMultiDummy(s, rec, appealNo);

            // appeal + single
            return BuildS52(s, rec, appealNo);
        }

        private byte[] BuildS52(NoticeSettings s, DummyRecipient rec, string appealNo)
        {
            var headerPath = Path.Combine(_env.WebRootPath, "Images", "Obj_Header.PNG");

            var preview = new Section52PreviewData
            {
                HeaderImagePath = headerPath,
                LetterDate = DateOnly.FromDateTime(s.LetterDate),
                IsReview = false,
                RecipientName = rec.Name,
                Email = rec.Email,

                PropertyDescription = "Sample Property Description",
                Town = "ROSEBANK",
                Erf = "201",
                Portion = "2",
                Re = "",

                ObjectionNo = "OBJ-GV23-0001",
                AppealNo = appealNo,

                Category = "Residential",
                Extent = "1000",
                MarketValue = "3000000",

                ValuationKey = "KEY-XXXX"
            };

            return _s52.BuildPreview(preview);
        }

        private byte[] BuildS52ReviewMultiDummy(NoticeSettings s, DummyRecipient rec, string appealNo)
        {
            var headerPath = Path.Combine(_env.WebRootPath, "Images", "Obj_Header.PNG");

            var preview = new Section52PreviewData
            {
                HeaderImagePath = headerPath,
                LetterDate = DateOnly.FromDateTime(s.LetterDate),
                IsReview = true,

                RecipientName = rec.Name,
                Email = rec.Email,

                PropertyDescription = "MULTI PROPERTY (SECTION 52 REVIEW – PREVIEW)",
                Town = "ROSEBANK",
                Erf = "201",
                Portion = "2",
                Re = "",

                ObjectionNo = "OBJ-GV23-MULTI-0001",
                AppealNo = appealNo,

                Category = "Residential",
                Extent = "1000",
                MarketValue = "3000000",

                Extent2 = "850",
                MarketValue2 = "2500000",

                Extent3 = "1200",
                MarketValue3 = "3400000",

                ValuationKey = "KEY-S52-REVIEW-MULTI"
            };

            return _s52.BuildPreview(preview);
        }

        private byte[] BuildS52ReviewSingleDummy(NoticeSettings s, DummyRecipient rec, string appealNo)
        {
            var headerPath = Path.Combine(_env.WebRootPath, "Images", "Obj_Header.PNG");

            var preview = new Section52PreviewData
            {
                HeaderImagePath = headerPath,
                LetterDate = DateOnly.FromDateTime(s.LetterDate),
                IsReview = true,

                RecipientName = rec.Name,
                Email = rec.Email,

                PropertyDescription = "SINGLE PROPERTY (SECTION 52 REVIEW – PREVIEW)",
                Town = "ROSEBANK",
                Erf = "201",
                Portion = "2",
                Re = "",

                ObjectionNo = "OBJ-GV23-REVIEW-0001",
                AppealNo = appealNo,

                Category = "Residential",
                Extent = "1000",
                MarketValue = "3000000",

                ValuationKey = "KEY-S52-REVIEW-SINGLE"
            };

            return _s52.BuildPreview(preview);
        }

        private byte[] BuildS52AppealMultiDummy(NoticeSettings s, DummyRecipient rec, string appealNo)
        {
            var headerPath = Path.Combine(_env.WebRootPath, "Images", "Obj_Header.PNG");

            var preview = new Section52PreviewData
            {
                HeaderImagePath = headerPath,
                LetterDate = DateOnly.FromDateTime(s.LetterDate),
                IsReview = false,

                RecipientName = rec.Name,
                Email = rec.Email,

                PropertyDescription = "MULTI PROPERTY (APPEAL DECISION – PREVIEW)",
                Town = "ROSEBANK",
                Erf = "201",
                Portion = "2",
                Re = "",

                ObjectionNo = "OBJ-GV23-APPEAL-MULTI-0001",
                AppealNo = appealNo,

                Category = "Residential",
                Extent = "1000",
                MarketValue = "3000000",

                Extent2 = "850",
                MarketValue2 = "2500000",

                Extent3 = "1200",
                MarketValue3 = "3400000",

                ValuationKey = "KEY-S52-APPEAL-MULTI"
            };

            return _s52.BuildPreview(preview);
        }

        public Task<NoticePreviewResult> BuildPreviewAsync(
    int settingsId,
    PreviewVariant variant,
    bool isMulti,
    CancellationToken ct)
        {
            var mode = isMulti ? PreviewMode.EmailMulti : PreviewMode.Single;
            return BuildPreviewAsync(settingsId, variant, mode, ct);
        }
        public async Task<NoticePreviewResult> BuildPreviewAsync(
    int settingsId,
    PreviewVariant variant,
    PreviewMode mode,
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

            if (roll is null)
                throw new InvalidOperationException("RollRegistry not found.");

            var (objNo, appNo) = _dummy.GetSampleRefs(roll.ShortCode);
            var recipient = _dummy.GetRecipient();
            var prop = _dummy.GetProperty();

            var headerPath = Path.Combine(_env.WebRootPath, "Images", "Obj_Header.PNG");

            var isEmailMulti = mode == PreviewMode.EmailMulti;
            var isSplitPdf = mode == PreviewMode.SplitPdf;

            // ✅ PDF selector
            var pdfBytes = settings.Notice switch
            {
                NoticeKind.S49 => isSplitPdf
    ? BuildS49SplitDummy(settings, roll.ShortCode, headerPath, recipient, objNo)
    : (isEmailMulti
        ? BuildS49MultiDummy(settings, roll.ShortCode, headerPath, recipient, objNo)
        : BuildS49(settings, roll.ShortCode, headerPath, recipient, objNo)),


                NoticeKind.S51 => isEmailMulti
                    ? BuildS51MultiDummy(settings, headerPath, recipient, objNo, prop)
                    : BuildS51(settings, headerPath, recipient, objNo, prop),

                NoticeKind.S52 => BuildS52Selector(settings, recipient, appNo, variant, isEmailMulti),

                // ✅ S53: SplitPdf overrides PDF only
                NoticeKind.S53 => isSplitPdf
                    ? BuildS53SplitDummy(settings, recipient, objNo)  // ✅ NEW
                    : (isEmailMulti ? BuildS53MultiDummy(settings, recipient, objNo)
                                    : BuildS53(settings, recipient, objNo)),

                NoticeKind.DJ => isEmailMulti
                    ? BuildDJMultiDummy(settings, headerPath, recipient, objNo, roll.ShortCode)
                    : BuildDJ(settings, headerPath, recipient, objNo, roll.ShortCode),

                NoticeKind.IN => BuildINSelector(settings, headerPath, recipient, objNo, roll.ShortCode, variant),

                NoticeKind.S78 => isEmailMulti
                    ? BuildS78MultiDummy(settings, headerPath, recipient, prop)
                    : BuildS78(settings, headerPath, recipient, prop),

                _ => throw new NotSupportedException($"Preview not implemented for notice {settings.Notice}.")
            };

            // ✅ Email selector:
            // SplitPdf must NOT change email. Only EmailMulti affects email grouping.
            var emailIsMulti = isEmailMulti;

            var emailReq = BuildEmailReq(settings, roll, recipient, prop, objNo, appNo, variant, emailIsMulti);
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

                RecipientName = recipient.Name,
                RecipientEmail = recipient.Email,
                AddressLine = recipient.AddressLine,
                SampleObjectionNo = objNo,
                SampleAppealNo = appNo,

                EmailSubject = subject,
                EmailBodyHtml = body,

                PdfBytes = pdfBytes,
                PdfFileName = $"{roll.ShortCode}_{settings.Notice}_PREVIEW.pdf"
            };
        }
        private byte[] BuildS53SplitDummy(NoticeSettings s, DummyRecipient rec, string objectionNo)
        {
            var appealClose = s.AppealCloseDate?.Date ?? s.LetterDate.Date.AddDays(45);

            decimal gvBcValue = 2_000_000m;
            decimal gvResValue = 1_000_000m;
            decimal gvBcExtent = 800m;
            decimal gvResExtent = 400m;

            decimal mvdBcValue = 2_200_000m;
            decimal mvdResValue = 1_050_000m;
            decimal mvdBcExtent = 800m;
            decimal mvdResExtent = 400m;

            decimal gvTotalValue = gvBcValue + gvResValue;
            decimal gvTotalExtent = gvBcExtent + gvResExtent;

            decimal mvdTotalValue = mvdBcValue + mvdResValue;
            decimal mvdTotalExtent = mvdBcExtent + mvdResExtent;

            string Money(decimal v) => "R " + v.ToString("N0").Replace(",", " ");
            string Num(decimal v) => v.ToString("N0").Replace(",", " ");

            var row = new Section53MvdRow
            {
                ObjectionNo = objectionNo,
                PropertyDesc = "SPLIT PROPERTY PREVIEW (TOTAL + COMPONENTS)",

                Addr1 = rec.Name,
                Addr2 = rec.AddressLine,
                Addr3 = "Jorissen Street",
                Addr4 = "Braamfontein",
                Addr5 = "2001",

                AppealCloseDate = appealClose,
                Section52Review = "No",
                ValuationKey = "VAL-KEY-S53-SPLIT",

                // ✅ Row1 = Multipurpose (Total)
                Gv_Category = "Multipurpose",
                Gv_Market_Value = Money(gvTotalValue),
                Gv_Extent = Num(gvTotalExtent),

                // ✅ Row2 = Business and Commercial
                Gv_Category2 = "Business and Commercial",
                Gv_Market_Value2 = Money(gvBcValue),
                Gv_Extent2 = Num(gvBcExtent),

                // ✅ Row3 = Residential
                Gv_Category3 = "Residential",
                Gv_Market_Value3 = Money(gvResValue),
                Gv_Extent3 = Num(gvResExtent),

                // MVD
                Mvd_Category = "Multipurpose",
                Mvd_Market_Value = Money(mvdTotalValue),
                Mvd_Extent = Num(mvdTotalExtent),

                Mvd_Category2 = "Business and Commercial",
                Mvd_Market_Value2 = Money(mvdBcValue),
                Mvd_Extent2 = Num(mvdBcExtent),

                Mvd_Category3 = "Residential",
                Mvd_Market_Value3 = Money(mvdResValue),
                Mvd_Extent3 = Num(mvdResExtent),
            };

            return _s53.BuildPreviewPdf(row, DateOnly.FromDateTime(s.LetterDate));
        }

        private byte[] BuildS49SplitDummy(NoticeSettings s, string rollShortCode, string headerPath, DummyRecipient rec, string objectionNo)
        {
            // ✅ must always be 4 rows, with your required order:
            // Multipurpose (sum), Business & Commercial, Residential, (4th row blank or other)
            var rows = new List<GV23_Notice.Services.Notices.Section49.Section49PropertyRow>();

            decimal bcValue = 2000000m;
            decimal resValue = 1000000m;
            decimal bcExtent = 800m;
            decimal resExtent = 400m;

            decimal totalValue = bcValue + resValue;
            decimal totalExtent = bcExtent + resExtent;

            string Money(decimal v) => "R " + v.ToString("N0").Replace(",", " ");
            string Num(decimal v) => v.ToString("N0").Replace(",", " ");

            rows.Add(new GV23_Notice.Services.Notices.Section49.Section49PropertyRow
            {
                Category = "Multipurpose",
                MarketValue = Money(totalValue),
                Extent = Num(totalExtent),
                Remarks = "Total (sum of Business & Commercial + Residential)"
            });

            rows.Add(new GV23_Notice.Services.Notices.Section49.Section49PropertyRow
            {
                Category = "Business and Commercial",
                MarketValue = Money(bcValue),
                Extent = Num(bcExtent),
                Remarks = ""
            });

            rows.Add(new GV23_Notice.Services.Notices.Section49.Section49PropertyRow
            {
                Category = "Residential",
                MarketValue = Money(resValue),
                Extent = Num(resExtent),
                Remarks = ""
            });

            // 4th row stays blank (padding will also ensure 4)
            rows.Add(new GV23_Notice.Services.Notices.Section49.Section49PropertyRow());

            var preview = new Section49PreviewData
            {
                HeaderImagePath = headerPath,
                SignaturePath = s.SignaturePath,
                LetterDate = s.LetterDate,
                InspectionStartDate = s.ObjectionStartDate ?? s.LetterDate,
                InspectionEndDate = s.ObjectionEndDate ?? s.LetterDate.AddDays(30),
                ExtendedEndDate = s.ExtensionDate,
                FinancialYearsText = s.FinancialYearsText ?? $"{new DateTime(DateTime.Today.Year, 7, 1):dd MMMM yyyy} – {new DateTime(DateTime.Today.Year + 1, 6, 30):dd MMMM yyyy}",

                RollHeaderText = $"{rollShortCode} ROLL (SPLIT PREVIEW)",

                ForceFourRows = true,
                PropertyRows = rows
            };

            return _s49.BuildPreview(preview);
        }

    }
}
