using GV23_Notice.Services.Notices.Section53.COJ_Notice_2026.Models.ViewModels.Section53;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace GV23_Notice.Services.Notices.Section53
{
    public sealed class Section53PdfService : ISection53PdfService
    {
        private readonly Section53PdfOptions _opt;
        private readonly IWebHostEnvironment _env;

        public Section53PdfService(IOptions<Section53PdfOptions> opt, IWebHostEnvironment env)
        {
            _opt = opt.Value;
            _env = env;
        }

        // ===========================
        // Public API (same pattern as S49/S51)
        // ===========================

        public byte[] BuildNoticePdf(Section53MvdRow row, DateOnly letterDate)
            => BuildNoticePdf(new List<Section53MvdRow> { row }, letterDate);

        public byte[] BuildNoticePdf(IReadOnlyList<Section53MvdRow> rows, DateOnly letterDate)
            => BuildPdf(rows, letterDate, isPreview: false);

        public byte[] BuildPreviewPdf(Section53MvdRow dummyRow, DateOnly letterDate)
            => BuildPdf(new List<Section53MvdRow> { dummyRow }, letterDate, isPreview: true);

        // ===========================
        // Core shared builder (ONE BODY)
        // ===========================
        private byte[] BuildPdf(IReadOnlyList<Section53MvdRow> rows, DateOnly letterDate, bool isPreview)
        {
            if (rows == null || rows.Count == 0)
                throw new InvalidOperationException("No rows provided for Section 53 PDF.");

            QuestPDF.Settings.License = LicenseType.Community;

            var model = new Model(rows, letterDate, _opt, _env);

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginLeft(30);
                    page.MarginRight(30);
                    page.MarginTop(10);
                    page.MarginBottom(10);
                    page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(10));

                    page.Content().Column(col =>
                    {
                        // =========================
                        // HEADER
                        // =========================
                        var headerPath = model.HeaderImageAbsolutePath;

                        if (!string.IsNullOrWhiteSpace(headerPath) && File.Exists(headerPath))
                        {
                            col.Item()
                               .Height(95)
                               .AlignCenter()
                               .Image(headerPath, ImageScaling.FitArea);

                            col.Item().PaddingTop(6);
                        }
                        else
                        {
                            col.Item()
                               .Border(1)
                               .Padding(6)
                               .Background(Colors.Grey.Lighten4)
                               .Text(t =>
                               {
                                   t.Span("HEADER IMAGE NOT FOUND").SemiBold();
                                   t.Line($"Relative: {model.HeaderImageRelativePath ?? "(null)"}");
                                   t.Line($"Absolute: {headerPath ?? "(null)"}");
                                   t.Line($"WebRoot: {_env.WebRootPath ?? "(null)"}");
                               });

                            col.Item().PaddingTop(6);
                        }

                        // ✅ PREVIEW BANNER (same idea as S49)
                        if (isPreview)
                        {
                            col.Item()
                               .Border(1)
                               .Padding(6)
                               .Background(Colors.Grey.Lighten4)
                               .AlignCenter()
                               .Text("TEMPLATE PREVIEW (DUMMY DATA)")
                               .SemiBold();

                            col.Item().PaddingTop(8);
                        }

                        // =========================
                        // ADDRESS LEFT / DATE RIGHT
                        // =========================
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Column(left =>
                            {
                                AddAddrLine(left, model.Addr1);
                                AddAddrLine(left, model.Addr2);
                                AddAddrLine(left, model.Addr3);
                                AddAddrLine(left, model.Addr4);
                                AddAddrLine(left, model.Addr5);
                            });

                            r.ConstantItem(180).AlignRight().Column(right =>
                            {
                                right.Item()
                                     .Text(model.LetterDate.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture))
                                     .FontSize(10);
                            });
                        });

                        col.Item().PaddingTop(10);

                        // =========================
                        // TITLE
                        // =========================
                        col.Item().AlignCenter().Text("NOTICE").SemiBold().FontSize(12);

                        col.Item().AlignCenter()
                            .Text("Notification of outcome of objection in terms of section 53(1) of the Municipal Property Rates Act, No.6 of 2004 as amended")
                            .SemiBold().FontSize(10);

                        col.Item().PaddingTop(6).LineHorizontal(1.5f);
                        col.Item().PaddingTop(10);

                        // =========================
                        // PROPERTY LINE
                        // =========================
                        col.Item().Text(t =>
                        {
                            t.Span("Property Description: ").SemiBold();
                            t.Span(model.PropertyDesc);
                        });

                        col.Item().PaddingTop(10);

                        // =========================
                        // INTRO
                        // =========================
                        col.Item().Text(t =>
                        {
                            t.Span("Notice is hereby given in terms of section 53(1) of the Municipal Property Rates Act No.6 of 2004 as amended, that the objection Nr. ");
                            t.Span(model.ObjectionNo).SemiBold();
                            t.Span(" against the entry of the above property in or omitted from the valuation roll has been considered by the Municipal Valuer. ");
                            t.Span("After reviewing the objection and reasons provided therein and the submission of the owner if a submission was made, together with the available market information, ");
                            t.Span("the Municipal Valuer’s decision is as follows:");
                        });

                        col.Item().PaddingTop(10);

                        // =========================
                        // GV TABLE
                        // =========================
                        col.Item().Text("Entry in Valuation Roll").SemiBold();
                        col.Item().PaddingTop(4);
                        col.Item().Element(e => BuildGvTable(e, model));

                        col.Item().PaddingTop(10);

                        // =========================
                        // MVD TABLE
                        // =========================
                        col.Item().Text("Municipal Valuer’s Decision (MVD)").SemiBold();
                        col.Item().PaddingTop(4);
                        col.Item().Element(e => BuildMvdTable(e, model));

                        col.Item().PaddingTop(12);

                        // =========================
                        // SECTION 52 REVIEW
                        // =========================
                        col.Item().Text(t =>
                        {
                            t.Span("Section 52 Review: ").SemiBold();
                            t.Span(model.Section52Review);
                        });

                        col.Item().PaddingTop(8);

                        col.Item().Text(
                                "Kindly note that in terms of Section 52 of the Municipal Property Rates Act No.6 of 2004 as amended, if the value has changed by more than 10% upwards or downwards, an automatic review by the Valuation Appeal Board will be conducted who may confirm, amend or revoke the decision.")
                            .SemiBold()
                            .Justify();

                        col.Item().PaddingTop(12);

                        // =========================
                        // RIGHT OF APPEAL
                        // =========================
                        col.Item().Text("Right of Appeal").SemiBold();

                        col.Item().PaddingTop(6).Text(t =>
                        {
                            t.Span("In terms of Section 54(1) an appeal to the Appeal Board against the above decision may be lodged in the prescribed manner online on the City’s online system on the following link: ");
                            t.Span(model.PortalUrl);
                            t.Span(" or at the following address: Valuation Services: Administration, 1st Floor, East Wing, Jorissen Place, 66 Jorissen Street, Braamfontein on or before 15:00 on ");
                            t.Span(model.AppealCloseDateText).SemiBold(); // bold date only ✅
                            t.Span(".");
                        });

                        col.Item().PaddingTop(10).Text(
                                "An acknowledgement letter will be provided and should be kept as proof that the appeal was submitted. Please include this notice when submitting your appeal for ease of reference. Kindly note that objections against issues other than the above (eg. Owners name, street address etc) are not dealt with as an objection, but will be forwarded to the relevant department as amended to the valuation roll in terms of the section 79 of above Act. If a representative is appointed, proof of authorisation must be attached to the appeal form.")
                            .Justify();

                        if (!string.IsNullOrWhiteSpace(model.ContactLine))
                            col.Item().PaddingTop(10).Text(model.ContactLine).FontSize(10);

                        // =========================
                        // SIGN OFF
                        // =========================
                        col.Item().PaddingTop(14).Text(model.MunicipalValuerName).SemiBold();
                        col.Item().Text(model.MunicipalValuerTitle).SemiBold();

                        col.Item().PaddingTop(10).AlignRight().Text(model.ValuationKey).FontSize(7);
                    });

                    page.Footer().AlignCenter().Column(f =>
                    {
                        f.Item().PaddingTop(6).AlignCenter()
                            .Text("_______________________________________________")
                            .FontSize(7)
                            .FontColor(Colors.Grey.Darken2);

                        f.Item()
                            .Text("This is an official document generated by the City of Johannesburg Valuation Services Department")
                            .FontSize(7)
                            .AlignCenter()
                            .FontColor(Colors.Grey.Darken2);

                        f.Item()
                            .Text($"Generated on: {model.LetterDate:dd MMMM yyyy}")
                            .FontSize(7)
                            .AlignCenter()
                            .FontColor(Colors.Grey.Darken2);
                    });
                });
            }).GeneratePdf();
        }

        // =========================================================
        // TABLE BUILDERS
        // =========================================================
        private static IContainer BuildGvTable(IContainer container, Model m)
        {
            container.Element(c =>
            {
                c.Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn();
                        cols.RelativeColumn();
                        cols.RelativeColumn();
                        cols.RelativeColumn();
                    });

                    table.Header(h =>
                    {
                        h.Cell().Element(Th).Text("MARKET VALUE");
                        h.Cell().Element(Th).Text("EXTENT");
                        h.Cell().Element(Th).Text("CATEGORY");
                        h.Cell().Element(Th).Text("REMARK");
                    });

                    AddSplitRowIfAny(table, m.Gv_Market_Value, m.Gv_Extent, m.Gv_Category);
                    AddSplitRowIfAny(table, m.Gv_Market_Value2, m.Gv_Extent2, m.Gv_Category2);
                    AddSplitRowIfAny(table, m.Gv_Market_Value3, m.Gv_Extent3, m.Gv_Category3);
                });
            });

            return container;
        }

        private static IContainer BuildMvdTable(IContainer container, Model m)
        {
            container.Element(c =>
            {
                c.Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn();
                        cols.RelativeColumn();
                        cols.RelativeColumn();
                        cols.RelativeColumn();
                    });

                    table.Header(h =>
                    {
                        h.Cell().Element(Th).Text("MARKET VALUE");
                        h.Cell().Element(Th).Text("EXTENT");
                        h.Cell().Element(Th).Text("CATEGORY");
                        h.Cell().Element(Th).Text("REMARK");
                    });

                    AddSplitRowIfAny(table, m.Mvd_Market_Value, m.Mvd_Extent, m.Mvd_Category);
                    AddSplitRowIfAny(table, m.Mvd_Market_Value2, m.Mvd_Extent2, m.Mvd_Category2);
                    AddSplitRowIfAny(table, m.Mvd_Market_Value3, m.Mvd_Extent3, m.Mvd_Category3);
                });
            });

            return container;
        }

        private static void AddSplitRowIfAny(TableDescriptor table, string? marketValue, string? extent, string? category)
        {
            bool any =
                !string.IsNullOrWhiteSpace(marketValue) ||
                !string.IsNullOrWhiteSpace(extent) ||
                !string.IsNullOrWhiteSpace(category);

            if (!any) return;

            table.Cell().Element(Td).Text(marketValue ?? "");
            table.Cell().Element(Td).Text(extent ?? "");
            table.Cell().Element(Td).Text(category ?? "");
            table.Cell().Element(Td).Text(""); // remark column intentionally empty
        }

        // =========================================================
        // HELPERS
        // =========================================================
        private static void AddAddrLine(ColumnDescriptor col, string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            col.Item().Text(line.Trim()).FontSize(10);
        }

        private static IContainer Th(IContainer c) =>
            c.Border(1)
             .Padding(4)
             .Background(Colors.Grey.Lighten3)
             .DefaultTextStyle(x => x.SemiBold().FontSize(9));

        private static IContainer Td(IContainer c) =>
            c.Border(1)
             .Padding(4)
             .DefaultTextStyle(x => x.FontSize(9));

        // =========================================================
        // MODEL (same as yours, but kept internal)
        // =========================================================
        private sealed record Model(IReadOnlyList<Section53MvdRow> Rows, DateOnly LetterDate, Section53PdfOptions Opt, IWebHostEnvironment Env)
        {
            private Section53MvdRow First => Rows[0];

            public string? HeaderImageRelativePath => Opt.HeaderImageRelativePath;
            public string HeaderImageAbsolutePath => ResolveWwwRootPath(Env, Opt.HeaderImageRelativePath);

            public string PortalUrl => Opt.PortalUrl ?? "https://objections.joburg.org.za/";
            public string ContactLine => Opt.ContactLine ?? "";
            public string MunicipalValuerName => Opt.MunicipalValuerName ?? "S. Faiaz";
            public string MunicipalValuerTitle => Opt.MunicipalValuerTitle ?? "Municipal Valuer";

            public string ObjectionNo => First.ObjectionNo ?? "";
            public string PropertyDesc => First.PropertyDesc ?? "";

            public string Addr1 => First.Addr1 ?? "";
            public string Addr2 => First.Addr2 ?? "";
            public string Addr3 => First.Addr3 ?? "";
            public string Addr4 => First.Addr4 ?? "";
            public string Addr5 => First.Addr5 ?? "";

            public string ValuationKey => First.ValuationKey ?? "";
            public string Section52Review => First.Section52Review ?? "";

            public string AppealCloseDateText =>
                First.AppealCloseDate.HasValue
                    ? First.AppealCloseDate.Value.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture)
                    : "";

            public string Gv_Market_Value => First.Gv_Market_Value ?? "";
            public string Gv_Market_Value2 => First.Gv_Market_Value2 ?? "";
            public string Gv_Market_Value3 => First.Gv_Market_Value3 ?? "";

            public string Gv_Extent => First.Gv_Extent ?? "";
            public string Gv_Extent2 => First.Gv_Extent2 ?? "";
            public string Gv_Extent3 => First.Gv_Extent3 ?? "";

            public string Gv_Category => First.Gv_Category ?? "";
            public string Gv_Category2 => First.Gv_Category2 ?? "";
            public string Gv_Category3 => First.Gv_Category3 ?? "";

            public string Mvd_Market_Value => First.Mvd_Market_Value ?? "";
            public string Mvd_Market_Value2 => First.Mvd_Market_Value2 ?? "";
            public string Mvd_Market_Value3 => First.Mvd_Market_Value3 ?? "";

            public string Mvd_Extent => First.Mvd_Extent ?? "";
            public string Mvd_Extent2 => First.Mvd_Extent2 ?? "";
            public string Mvd_Extent3 => First.Mvd_Extent3 ?? "";

            public string Mvd_Category => First.Mvd_Category ?? "";
            public string Mvd_Category2 => First.Mvd_Category2 ?? "";
            public string Mvd_Category3 => First.Mvd_Category3 ?? "";
        }

        private static string ResolveWwwRootPath(IWebHostEnvironment env, string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return "";

            relativePath = relativePath.Trim().TrimStart('/', '\\');
            var parts = relativePath.Replace('\\', '/')
                                    .Split('/', StringSplitOptions.RemoveEmptyEntries);

            return Path.Combine(new[] { env.WebRootPath }.Concat(parts).ToArray());
        }
    }
}
