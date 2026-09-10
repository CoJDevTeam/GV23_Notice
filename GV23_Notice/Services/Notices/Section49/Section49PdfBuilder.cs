using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace GV23_Notice.Services.Notices.Section49
{
    public sealed class Section49PdfBuilder : ISection49PdfBuilder
    {
        public byte[] BuildNotice(Section49PdfData data, Section49NoticeContext ctx)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            if (string.IsNullOrWhiteSpace(ctx.HeaderImagePath))
                throw new InvalidOperationException("HeaderImagePath is required for Section49 PDF.");

            var culture = CultureInfo.GetCultureInfo("en-ZA");

            var title12 = TextStyle.Default.FontFamily("Arial").FontSize(12).SemiBold();
            var sub10b = TextStyle.Default.FontFamily("Arial").FontSize(10).SemiBold();
            var body9 = TextStyle.Default.FontFamily("Arial").FontSize(9);
            var body9b = TextStyle.Default.FontFamily("Arial").FontSize(9).SemiBold();
            var small7 = TextStyle.Default.FontFamily("Arial").FontSize(7).FontColor(Colors.Grey.Darken2);



            var red7b = TextStyle.Default
                .FontFamily("Arial")
                .FontSize(7)
                .SemiBold()
                .FontColor(Colors.Red.Medium);


            var inspectionWindowText = ctx.ExtendedEndDate.HasValue
                ? $"{ctx.InspectionStartDate:dd MMMM yyyy} – {ctx.ExtendedEndDate:dd MMMM yyyy} until 15:00"
                : $"{ctx.InspectionStartDate:dd MMMM yyyy} – {ctx.InspectionEndDate:dd MMMM yyyy} until 15:00";

            var closingDate = ctx.ExtendedEndDate ?? ctx.InspectionEndDate;

            static string Safe(string? s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim();
            var portalUrl = string.IsNullOrWhiteSpace(ctx.PortalUrl) ? "https://objections.joburg.org.za/" : ctx.PortalUrl.Trim();

            // Recipient / postal address display.
            // These are PDF-only fallbacks and are never written back to the DB.
            var greeting = "Dear Property Owner";

            var postalLines = new List<string>();

            if (!string.IsNullOrWhiteSpace(data.Addr1))
                postalLines.Add(Safe(data.Addr1));

            if (!string.IsNullOrWhiteSpace(data.Addr2))
                postalLines.Add(Safe(data.Addr2));

            if (!string.IsNullOrWhiteSpace(data.Addr3))
                postalLines.Add(Safe(data.Addr3));

            if (!string.IsNullOrWhiteSpace(data.Addr4))
                postalLines.Add(Safe(data.Addr4));

            if (!string.IsNullOrWhiteSpace(data.Addr5))
                postalLines.Add(Safe(data.Addr5));

            // Professional display fallback when no postal address exists.
            // Database fields remain NULL/blank.
            if (postalLines.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(data.PropertyDesc))
                    postalLines.Add(Safe(data.PropertyDesc));

                postalLines.Add("JOHANNESBURG");
            }

            var physicalAddress =
                !string.IsNullOrWhiteSpace(data.PhysicalAddress)
                    ? Safe(data.PhysicalAddress)
                    : Safe(data.PropertyDesc);

            // rollDisplayName  – uppercased for headings  e.g. "SUPPLEMENTARY VALUATION ROLL 1"
            // rollDisplayTitle – proper case for body text e.g. "Supplementary Valuation Roll 1"
            var rollDisplayName = Safe(ctx.RollHeaderText).ToUpper();
            var rollDisplayTitle = Safe(ctx.RollHeaderText);

            var rollDisplayWithReference =
                string.IsNullOrWhiteSpace(rollDisplayTitle)
                    ? "GVR2023"
                    : rollDisplayTitle.Contains(
                        "GVR2023",
                        StringComparison.OrdinalIgnoreCase)
                        ? rollDisplayTitle
                        : $"{rollDisplayTitle} (GVR2023)";

            var rollDisplayWithReferenceUpper =
                rollDisplayWithReference.ToUpperInvariant();
            // Ensure rows exist
            var rows = data.PropertyRows ?? new List<Section49PropertyRow>();
            if (rows.Count == 0)
            {
                // fallback row (should not happen if DB loader works)
                rows.Add(new Section49PropertyRow { Category = "", MarketValue = "", Extent = "", Remarks = "", WEFDate = null });
            }

            // Force 4 rows for split/multipurpose
            if (data.ForceFourRows)
            {
                while (rows.Count < 4) rows.Add(new Section49PropertyRow());
                if (rows.Count > 4) rows = rows.Take(4).ToList();
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

                    // ✅ FOOTER: bottom-left official text + bottom-right valuation key
                    page.Footer()
         .PaddingTop(8)
         .AlignCenter()
         .Text(t =>
         {
             t.Line("_______________________________________________").Style(small7);

             t.Line("This is an official document generated by the City of Johannesburg Valuation Services Department")
              .Style(small7);

             t.Line($"Generated on: {ctx.LetterDate:dd MMMM yyyy}")
              .Style(small7);

             t.Line(Safe(data.ValuationKey))
              .Style(red7b.SemiBold());
         });
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
                                foreach (var line in postalLines)
                                {
                                    t.Span(line + "\n")
                                     .Style(sub10b);
                                }
                            });

                            r.ConstantItem(180).AlignRight()
                                .Text(ctx.LetterDate.ToString("dd MMMM yyyy", culture))
                                .Style(body9);
                        });

                        // ===== TITLES =====
                        col.Item().AlignCenter().Text("CITY OF JOHANNESBURG").Style(title12);
                        col.Item().AlignCenter().Text(
                                       $"PUBLIC NOTICE CALLING FOR INSPECTION OF THE {rollDisplayWithReferenceUpper} AND LODGING OF OBJECTIONS"
                                   ).Style(sub10b);

                        col.Item().LineHorizontal(1.5f).LineColor(Colors.Grey.Darken2);
                        col.Item().PaddingBottom(6);

                        col.Item().PaddingTop(3).Text(greeting).FontFamily("Arial").FontSize(9).Bold();

                        // ===== MAIN NOTICE PARAGRAPH (mixed bold) =====
                        col.Item().Text(t =>
                        {
                            t.Span(
                                "Notice is hereby given in terms of " +
                                "Section 49(1)(a)(i) read together with " +
                                "section 78(2) of the ")
                                .Style(body9);

                            t.Span(
                                "Local Government: Municipal Property " +
                                "Rates Act No. 6 of 2004")
                                .Style(body9b);

                            t.Span(
                                $" as amended hereinafter referred to as " +
                                $"the \"Act\", that the " +
                                $"{rollDisplayWithReference} for the " +
                                $"financial years ")
                                .Style(body9);

                            t.Span(
                                Safe(ctx.FinancialYearsText))
                                .Style(body9b);

                            t.Span(
                                " is open for public inspection on the website ")
                                .Style(body9);

                            t.Span(
                                portalUrl)
                                .Style(body9b);

                            t.Span(
                                " and at the address listed below from ")
                                .Style(body9);

                            t.Span(
                                $"{ctx.InspectionStartDate:dd MMMM yyyy} – " +
                                $"{closingDate:dd MMMM yyyy} until 15:00 pm")
                                .Style(body9b);

                            t.Span(".")
                                .Style(body9);
                        });

                        col.Item().Text($"An invitation is hereby made in terms of section 49(1)(a)(ii) read together with section 78(2) of the Act to any owner of property or other person who so desires that may wish to lodge an objection with the Municipal Manager in respect of any matter reflected in, or omitted from, the {rollDisplayWithReference}. The objection must be submitted within the above mentioned inspection period."
                        ).Style(body9).Justify();

                        col.Item().Text(t =>
                        {
                            t.Span(
                                "Attention is specifically drawn to the fact that " +
                                "in terms of section 50(2) of the Act an objection " +
                                "must be in relation to a ")
                                .Style(body9);

                            t.Span(
                                "specific individual property")
                                .Style(body9b);

                            t.Span(
                                $" and not against the {rollDisplayWithReference} " +
                                "as such. The lodging of objections in terms of " +
                                "Chapter 4(d) of the Regulations to the Act can " +
                                "be done at the address below or preferably " +
                                "online at ")
                                .Style(body9);

                            t.Span(portalUrl)
                                .Style(body9b);

                            t.Span(".")
                                .Style(body9);
                        });

                        col.Item().Text(
                            "The completed forms could be returned to the following address or preferably submitted online on the online objection system."
                        ).Style(body9);

                        col.Item()
      .Background(
          Color.FromRGB(
              240,
              240,
              240))
      .Padding(4)
      .Text(t =>
      {
          t.Span(
              "Valuation Services: Administration - ")
              .Style(body9b);

          t.Span(
              "1st Floor, East Wing, 66 Jorissen Street, " +
              "Braamfontein, Johannesburg, South Africa")
              .Style(body9);
      });
                        col.Item().Text(
                                    "The acknowledgement letter will be generated by the online system and should be kept as proof that the objection was submitted."
                                ).Style(body9);

                        //// ===== PAGE BREAK =====
                        //col.Item().PageBreak();

                        // ===== PAGE 2 =====
                        col.Item().LineHorizontal(1.5f).LineColor(Colors.Grey.Darken2);

                        col.Item().AlignCenter().Text(
                            $"PROPERTY DETAILS AS LISTED IN {rollDisplayWithReferenceUpper}"
                        ).Style(sub10b);

                        // Property box (light blue-ish)
                        col.Item()
      .Background(
          Color.FromRGB(
              245,
              250,
              255))
      .Padding(8)
      .Row(row =>
      {
          row.RelativeItem(1.2f)
              .Text(t =>
              {
                  t.Span("Property Description: ")
                      .SemiBold()
                      .Style(body9);

                  t.Span(
                      Safe(data.PropertyDesc))
                      .Style(body9);
              });

          row.RelativeItem(1f)
              .Text(t =>
              {
                  t.Span("Physical Address: ")
                      .SemiBold()
                      .Style(body9);

                  t.Span(
                      physicalAddress)
                      .Style(body9);
              });
      });
                        // Property table — styled to match Section 53 table
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn();    // Property Category
                                c.ConstantColumn(80);  // Area/m²
                                c.ConstantColumn(110); // Market Value
                                c.RelativeColumn();    // With Effective Date
                            });

                            // Header — identical to S53 BlueHeaderCell style
                            table.Header(h =>
                            {
                                void HCell(string text) =>
                                    h.Cell()
                                     .Border(1)
                                     .Background(Color.FromRGB(70, 130, 180))
                                     .PaddingVertical(4).PaddingHorizontal(6)
                                     .Text(text)
                                     .FontFamily("Arial").FontSize(9).SemiBold()
                                     .FontColor(Colors.White);

                                HCell("Property Category");
                                HCell("Area/m²");
                                HCell("Market Value");
                                HCell("With Effective Date");
                            });

                            // Data cell helper — identical to S53 CellBase + FontSize(9)
                            void DataCell(string? text, bool right = false, bool center = false)
                            {
                                var c = table.Cell().Border(1).PaddingVertical(4).PaddingHorizontal(6);
                                if (right) c = c.AlignRight();
                                else if (center) c = c.AlignCenter();
                                c.Text(Safe(text)).FontFamily("Arial").FontSize(9);
                            }

                            foreach (var rr in rows)
                            {
                                DataCell(rr.Category);

                                DataCell(
                                    rr.Extent,
                                    right: true);

                                DataCell(
                                    rr.MarketValue,
                                    right: true);

                                DataCell(
                                    rr.EffectiveDate,
                                    center: true);
                            }
                        });

                        // Closing date
                        col.Item()
                            .PaddingTop(10)
                            .AlignCenter()
                           .Text(
    $"CLOSING DATE FOR OBJECTIONS IS " +
    $"15:00 pm ON {closingDate:dd MMMM yyyy}"
        .ToUpper()
)
                            .Style(
                                body9b.FontColor(
                                    Colors.Red.Medium));


                        // Signature remains on page 1 only.
                        var signatureFile =
    ResolveSignatureFile(
        ctx.SignaturePath);

                        if (!string.IsNullOrWhiteSpace(
                                signatureFile))
                        {
                            /*
                             * Multi / split properties use more vertical space
                             * because the valuation table can contain up to 4 rows.
                             *
                             * Keep the signature compact so that it remains on
                             * page 1 and does not create an empty second page.
                             */
                            var isMulti =
                                data.ForceFourRows ||
                                rows.Count > 1;

                            var signatureWidth =
                                isMulti
                                    ? 220f
                                    : 260f;

                            var signatureHeight =
                                isMulti
                                    ? 100f
                                    : 100f;

                            var signatureTopPadding =
                                isMulti
                                    ? 4f
                                    : 10f;

                            col.Item()
                                .PaddingTop(
                                    signatureTopPadding)
                                .PaddingBottom(2)
                                .AlignLeft()
                                .Width(
                                    signatureWidth)
                                .Height(
                                    signatureHeight)
                                .Image(
                                    signatureFile,
                                    ImageScaling.FitArea);
                        }
                        // ====================================================
                        // PAGE 2 - INFORMATION GUIDANCE
                        // ====================================================
                        col.Item().PageBreak();

                        col.Item()
                            .PaddingTop(8)
                            .Table(table =>
                            {
                                table.ColumnsDefinition(c =>
                                {
                                    c.RelativeColumn();
                                    c.RelativeColumn();
                                });

                                table.Cell()
                                    .ColumnSpan(2)
                                    .Border(1)
                                    .Padding(4)
                                    .AlignCenter()
                                    .Text("NB.")
                                    .FontFamily("Arial")
                                    .FontSize(10)
                                    .SemiBold();

                                table.Cell()
                                    .Border(1)
                                    .Padding(6)
                                    .Text(t =>
                                    {
                                        t.Span("Residential properties information that will assist the Municipal Valuer to reach a reasonable decision is as follows:\n\n")
                                            .Style(body9);

                                        t.Span("• Market evidence (list of sold properties within the immediate area as at the valuation date 1 July 2022)\n")
                                            .Style(body9);
                                        t.Span("• Details of the property:\n").Style(body9);
                                        t.Span("• Number of bedrooms and bathrooms\n").Style(body9);
                                        t.Span("• Improvements\n").Style(body9);
                                        t.Span("• Swimming pool, etc.\n").Style(body9);
                                        t.Span("• The age of the improvements\n").Style(body9);
                                        t.Span("• Any adverse conditions that might affect the value\n").Style(body9);
                                        t.Span("• Building sizes\n").Style(body9);
                                        t.Span("• Building types (garage, granny flat) etc\n").Style(body9);
                                        t.Span("• Any other additional information\n")
                                            .Style(body9);
                                        t.Span("OR\n").Style(body9b);
                                        t.Span("• Motivated valuation report from registered Valuer")
                                            .Style(body9);
                                    });

                                table.Cell()
                                    .Border(1)
                                    .Padding(6)
                                    .Text(t =>
                                    {
                                        t.Span("Business properties information that will assist the Appeal Board to reach a reasonable decision is as follows:\n\n")
                                            .Style(body9);

                                        t.Span("• Rent Roll (if there are tenants)\n").Style(body9);
                                        t.Span("• Size of building (if there are no tenants in the building)\n").Style(body9);
                                        t.Span("• Actual use of building\n").Style(body9);
                                        t.Span("• Income / Expenditure\n").Style(body9);
                                        t.Span("• Number of parking bays\n").Style(body9);
                                        t.Span("• Condition of the building (attach photos)\n").Style(body9);
                                        t.Span("• Any other additional information\n")
                                            .Style(body9);
                                        t.Span("OR\n").Style(body9b);
                                        t.Span("• Motivated valuation report from registered Valuer")
                                            .Style(body9);
                                    });

                                table.Cell()
                                    .ColumnSpan(2)
                                    .Border(1)
                                    .Padding(4)
                                    .AlignCenter()
                                    .Text("REPRESENTATIVES:")
                                    .FontFamily("Arial")
                                    .FontSize(10)
                                    .SemiBold();

                                table.Cell()
                                    .ColumnSpan(2)
                                    .Border(1)
                                    .Padding(6)
                                    .AlignCenter()
                                    .Text("Letter of authorisation MUST be signed by the registered owner and attached to the objection form.")
                                    .FontFamily("Arial")
                                    .FontSize(9).Style(
                                body9b.FontColor(
                                    Colors.Red.Medium));
                            });

                        col.Item()
                            .PaddingTop(10)
                            .Text("For further enquiries please contact:")
                            .Style(body9b);

                        col.Item()
                            .Text("• valuationenquiries@joburg.org.za")
                            .Style(body9);
                    });
                });
            });

            return doc.GeneratePdf();
        }
        private static string? ResolveSignatureFile(
    string? configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
                return null;

            var path =
                configuredPath.Trim();

            // Config can point directly to an image.
            if (File.Exists(path))
                return path;

            // SignaturePath can point directly to an image or to the
            // latest Date Configuration version folder.
            if (!Directory.Exists(path))
                return null;

            var allowed =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase)
                {
            ".png",
            ".jpg",
            ".jpeg",
            ".bmp"
                };

            return Directory
                .EnumerateFiles(
                    path,
                    "*.*",
                    SearchOption.TopDirectoryOnly)
                .Where(x =>
                    allowed.Contains(
                        Path.GetExtension(x)))
                .OrderBy(x => x)
                .FirstOrDefault();
        }
    }
}