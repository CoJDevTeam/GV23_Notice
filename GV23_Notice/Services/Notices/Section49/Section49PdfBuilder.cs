using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace GV23_Notice.Services.Notices.Section49
{
    public sealed class Section49PdfBuilder : ISection49PdfBuilder
    {
        public byte[] BuildNotice(NoticeAttributesModel data, Section49NoticeContext ctx)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            if (string.IsNullOrWhiteSpace(ctx.HeaderImagePath))
                throw new InvalidOperationException("HeaderImagePath is required for Section49 PDF.");

            var culture = CultureInfo.GetCultureInfo("en-ZA");

            // iText used Arial 9/10/12 etc — we mimic with styles
            var title12 = TextStyle.Default.FontFamily("Arial").FontSize(12).SemiBold();
            var sub10b = TextStyle.Default.FontFamily("Arial").FontSize(10).SemiBold();
            var body9 = TextStyle.Default.FontFamily("Arial").FontSize(9);
            var body9b = TextStyle.Default.FontFamily("Arial").FontSize(9).SemiBold();
            var small7 = TextStyle.Default.FontFamily("Arial").FontSize(7).FontColor(Colors.Grey.Darken2);

            var inspectionWindowText = ctx.ExtendedEndDate.HasValue
                ? $"{ctx.InspectionStartDate:dd MMMM yyyy} – {ctx.ExtendedEndDate:dd MMMM yyyy} until 15:00"
                : $"{ctx.InspectionStartDate:dd MMMM yyyy} – {ctx.InspectionEndDate:dd MMMM yyyy} until 15:00";

            var closingDate = ctx.ExtendedEndDate ?? ctx.InspectionEndDate;

            static string Safe(string? s) => string.IsNullOrWhiteSpace(s) ? "" : s;

            // ✅ Resolve table rows:
            // - If SplitPdf mode sends rows, use them.
            // - Else fallback to the single row from NoticeAttributesModel.
            var rows = (ctx.PropertyRows is { Count: > 0 })
                ? ctx.PropertyRows.Take(4).ToList()
                : new List<Section49PropertyRow>
                {
                    new Section49PropertyRow
                    {
                        MarketValue = Safe(data.MarketValue),
                        Extent = Safe(data.RateableArea),
                        Category = Safe(data.CatDesc),
                        Remarks = Safe(data.Reason)
                    }
                };

            // ✅ Pad to exactly 4 rows when Split is required
            if (ctx.ForceFourRows)
            {
                while (rows.Count < 4)
                    rows.Add(new Section49PropertyRow());
            }

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginLeft(30);
                    page.MarginRight(30);
                    page.MarginTop(10);
                    page.MarginBottom(10);

                    page.Content().Column(col =>
                    {
                        col.Spacing(6);

                        // ===== HEADER IMAGE =====
                        col.Item().Image(ctx.HeaderImagePath, ImageScaling.FitWidth);

                        // ===== ADDRESS + DATE =====
                        col.Item().PaddingTop(6).Row(r =>
                        {
                            r.RelativeItem().Text(t =>
                            {
                                t.Span(Safe(data.ADDR1) + "\n").Style(sub10b);
                                if (!string.IsNullOrWhiteSpace(data.ADDR2)) t.Span(Safe(data.ADDR2) + "\n").Style(sub10b);
                                if (!string.IsNullOrWhiteSpace(data.ADDR3)) t.Span(Safe(data.ADDR3) + "\n").Style(sub10b);
                                if (!string.IsNullOrWhiteSpace(data.ADDR4)) t.Span(Safe(data.ADDR4) + "\n").Style(sub10b);
                                if (!string.IsNullOrWhiteSpace(data.ADDR5)) t.Span(Safe(data.ADDR5) + "\n").Style(sub10b);
                            });

                            r.ConstantItem(180)
                                .AlignRight()
                                .Text(ctx.LetterDate.ToString("dd MMMM yyyy", culture))
                                .Style(body9);
                        });

                        // ===== TITLES =====
                        col.Item().AlignCenter().Text("CITY OF JOHANNESBURG").Style(title12);

                        col.Item().AlignCenter().Text(
                            "PUBLIC NOTICE CALLING FOR INSPECTION OF THE SUPPLEMENTARY VALUATION\n" +
                            "ROLL AND LODGING OF OBJECTIONS"
                        ).Style(sub10b);

                        col.Item().LineHorizontal(1.5f).LineColor(Colors.Grey.Darken2);
                        col.Item().PaddingBottom(6);

                        // ===== MAIN NOTICE PARAGRAPH (mixed bold) =====
                        col.Item().Text(t =>
                        {
                            t.Span("Notice is hereby given in terms of Section 49(1)(a)(i) read together with section 78(2) of the ").Style(body9);
                            t.Span("Local Government: Municipal Property Rates Act No. 6 of 2004").Style(body9b);
                            t.Span(" as amended hereinafter referred to as the \"Act\", that the supplementary valuation roll for the financial years ").Style(body9);
                            t.Span(ctx.FinancialYearsText).Style(body9b);
                            t.Span(" is open for public inspection at the centre listed below, from ").Style(body9);
                            t.Span(inspectionWindowText).Style(body9b);
                            t.Span(". In addition, the valuation roll is available on the City's website ").Style(body9);
                            t.Span("www.joburg.org.za").Style(body9b);
                            t.Span(", under the GVR Online tile on the home page.\n").Style(body9);
                        });

                        // Invitation paragraph
                        col.Item().Text(
                            "An invitation is hereby made in terms of section 49(1)(a)(ii) read together with section 78(2) of the Act to any owner of property or other person who so desires that may wish to lodge an objection with the Municipal Manager in respect of any matter reflected in, or omitted from, the supplementary valuation roll. The objection must be submitted within the above mentioned inspection period."
                        ).Style(body9).Justify();

                        // Attention paragraph (mixed bold)
                        col.Item().Text(t =>
                        {
                            t.Span("Attention is specifically drawn to the fact that in terms of section 50(2) of the Act an objection must be in relation to a ").Style(body9);
                            t.Span("specific individual property").Style(body9b);
                            t.Span(" and not against the supplementary roll as such. The lodging of objections in terms of Chapter 4(d) of the Regulations to the Act can be done at the centre listed below or preferably online at ").Style(body9);
                            t.Span("www.joburg.org.za").Style(body9b);
                            t.Span(", under the GVR Online tile on the home page.").Style(body9);
                        });

                        // Submission
                        col.Item().Text(
                            "The completed forms could be returned to the following address or preferably submitted online on the online objection system."
                        ).Style(body9);

                        // Address box (grey background)
                        col.Item().Background(Color.FromRGB(240, 240, 240))
                            .Padding(8)
                            .Column(box =>
                            {
                                box.Item().Text("Valuation Services: Administration").Style(body9b);
                                box.Item().Text("Jorissen Place, 66 Jorissen Street, Braamfontein, East Wing, 1st Floor").Style(body9);
                            });

                        col.Item().Text(
                            "The acknowledgement letter will be generated by the online system and should be kept as proof that the objection was submitted."
                        ).Style(body9);

                        // ===== PAGE BREAK =====
                        col.Item().PageBreak();

                        // ===== PAGE 2 =====
                        col.Item().LineHorizontal(1.5f).LineColor(Colors.Grey.Darken2);

                        col.Item().AlignCenter().Text(
                            $"PROPERTY DETAILS AS LISTED IN {ctx.RollHeaderText}"
                        ).Style(sub10b);

                        // Property box (light blue-ish)
                        col.Item()
                            .Background(Color.FromRGB(245, 250, 255))
                            .Padding(8)
                            .Text($"Property Description: {Safe(data.PropertyDesc)} | Physical Address: {Safe(data.LisStreetAddress)}")
                            .Style(body9);

                        // ✅ Property table (supports 4 split rows)
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(110); // Market Value
                                c.ConstantColumn(80);  // Extent
                                c.RelativeColumn();    // Category
                                c.RelativeColumn();    // Remarks
                            });

                            void HeaderCell(string text) =>
                                table.Cell().Background(Color.FromRGB(70, 130, 180))
                                    .PaddingVertical(4).PaddingHorizontal(4)
                                    .AlignCenter()
                                    .Text(text)
                                    .FontFamily("Arial").FontSize(8).SemiBold()
                                    .FontColor(Colors.White);

                            HeaderCell("Market Value");
                            HeaderCell("Extent");
                            HeaderCell("Property Category");
                            HeaderCell("Remarks");

                            foreach (var rr in rows)
                            {
                                var mv = Safe(rr.MarketValue);

                                // keep consistent formatting (avoid "R R ...")
                                var mvText =
                                    string.IsNullOrWhiteSpace(mv) ? "" :
                                    mv.TrimStart().StartsWith("R", StringComparison.OrdinalIgnoreCase) ? mv :
                                    $"R {mv}";

                                table.Cell().Padding(4).AlignRight()
                                    .Text(mvText)
                                    .FontFamily("Arial").FontSize(7);

                                table.Cell().Padding(4).AlignCenter()
                                    .Text(Safe(rr.Extent))
                                    .FontFamily("Arial").FontSize(7);

                                table.Cell().Padding(4)
                                    .Text(Safe(rr.Category))
                                    .FontFamily("Arial").FontSize(7);

                                table.Cell().Padding(4)
                                    .Text(Safe(rr.Remarks))
                                    .FontFamily("Arial").FontSize(7);
                            }
                        });

                        // Closing date
                        col.Item().PaddingTop(10).AlignCenter().Text(
                            $"⚠ CLOSING DATE FOR OBJECTIONS IS 15:00 ON {closingDate:dd MMMM yyyy}"
                        ).Style(body9b);

                        // ✅ Signature (small + left)
                        if (!string.IsNullOrWhiteSpace(ctx.SignaturePath) && File.Exists(ctx.SignaturePath))
                        {
                            col.Item().PaddingTop(10).Row(r =>
                            {
                                r.ConstantItem(160)
                                    .AlignLeft()
                                    .Height(45)
                                    .Image(ctx.SignaturePath, ImageScaling.FitArea);

                                r.RelativeItem(); // spacer
                            });
                        }

                        // Footer (as body content like your iText version)
                        col.Item().PaddingTop(10).AlignCenter().Text(
                            "_______________________________________________\n" +
                            "This is an official document generated by the City of Johannesburg Valuation Services Department\n" +
                            $"Generated on: {DateTime.Now:dd MMMM yyyy}"
                        ).Style(small7);
                    });
                });
            });

            return doc.GeneratePdf();
        }

        public byte[] BuildPreview(Section49PreviewData data)
        {
            // Build dummy NoticeAttributesModel so the SAME template renders fully for Step2 approval
            var noticeData = new NoticeAttributesModel
            {
                ADDR1 = "Notice Sample",
                ADDR2 = "66 Jorissen Place",
                ADDR3 = "Jorissen Street",
                ADDR4 = "Braamfontein",
                ADDR5 = "",

                PropertyDesc = "PORTION 2 ERF 201 ROSEBANK",
                LisStreetAddress = "ROSEBANK",

                // fallback single row values
                MarketValue = "349 610 000",
                RateableArea = "2000",
                CatDesc = "Business and Commercial",
                Reason = "Sample remarks for preview"
            };

            var ctx = new Section49NoticeContext
            {
                LetterDate = data.LetterDate,
                HeaderImagePath = data.HeaderImagePath,
                InspectionStartDate = data.InspectionStartDate,
                InspectionEndDate = data.InspectionEndDate,
                ExtendedEndDate = data.ExtendedEndDate,
                FinancialYearsText = data.FinancialYearsText,
                RollHeaderText = data.RollHeaderText,
                SignaturePath = data.SignaturePath,

                // ✅ new for split
                ForceFourRows = data.ForceFourRows,
                PropertyRows = data.PropertyRows
            };

            return BuildNotice(noticeData, ctx);
        }
    }
}





