using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace GV23_Notice.Services.Notices.Section52
{
    public sealed class Section52PdfService : ISection52PdfService
    {
        private readonly IWebHostEnvironment _env;

        public Section52PdfService(IWebHostEnvironment env) => _env = env;

        public byte[] BuildDecisionPdf(AppealDecisionRow model, DateOnly letterDate)
        {
            var headerPath = Path.Combine(_env.WebRootPath ?? "", "Images", "Obj_Header.PNG");
            var ctx = new Section52PdfContext { HeaderImagePath = headerPath, LetterDate = letterDate };
            return BuildPdf(model, ctx, isPreview: false);
        }

        public byte[] BuildNotice(AppealDecisionRow row, Section52PdfContext ctx)
            => BuildPdf(row, ctx, isPreview: false);

        public byte[] BuildPreview(Section52PreviewData preview)
        {
            var row = new AppealDecisionRow
            {
                A_UserID = preview.IsReview ? "System_Generated" : "User",

                ADDR1 = preview.RecipientName,
                ADDR2 = "XXXX",
                ADDR3 = "XXXX",
                ADDR4 = "XXXX",
                ADDR5 = "XXXX",

                Email = preview.Email,
                Property_desc = preview.PropertyDescription,

                Town = preview.Town,
                ERF = preview.Erf,
                PTN = preview.Portion,
                RE = preview.Re,

                Objection_No = preview.ObjectionNo,
                Appeal_No = preview.AppealNo,

                App_Category = preview.Category,
                //App_Category2 = preview.cat,
                //App_Category3 = preview.Category3,

                App_Extent = preview.Extent,
                App_Extent2 = preview.Extent2,
                App_Extent3 = preview.Extent3,

                App_Market_Value = preview.MarketValue,
                App_Market_Value2 = preview.MarketValue2,
                App_Market_Value3 = preview.MarketValue3,

                valuation_Key = preview.ValuationKey
            };

            var ctx = new Section52PdfContext
            {
                HeaderImagePath = preview.HeaderImagePath,
                LetterDate = preview.LetterDate
            };

            return BuildPdf(row, ctx, isPreview: true);
        }

        private static byte[] BuildPdf(AppealDecisionRow model, Section52PdfContext ctx, bool isPreview)
        {
            if (model is null) throw new ArgumentNullException(nameof(model));
            if (ctx is null) throw new ArgumentNullException(nameof(ctx));
            if (string.IsNullOrWhiteSpace(ctx.HeaderImagePath))
                throw new InvalidOperationException("HeaderImagePath is required for Section52 PDF.");

            static string Safe(string? s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim();

            var headerPath = ctx.HeaderImagePath;
            var isReview = string.Equals(model.A_UserID, "System_Generated", StringComparison.OrdinalIgnoreCase);

            var title = isReview
                ? "VALUATION APPEAL BOARD: OUTCOME - SECTION 52 REVIEWS DECISIONS FOR THE GENERAL VALUATION ROLL 2023 (GV2023)"
                : "VALUATION APPEAL BOARD: OUTCOME - APPEAL DECISIONS FOR THE GENERAL VALUATION ROLL 2023 (GV2023)";

            var kindShort = isReview ? "Section 52 Review" : "Appeal";

            var dateText = ctx.LetterDate
                .ToDateTime(TimeOnly.MinValue)
                .ToString("dd MMMM yyyy", CultureInfo.GetCultureInfo("en-ZA"));

            var recipient = Safe(model.ADDR1);
            var greeting = string.IsNullOrWhiteSpace(recipient) ? "Dear Sir/Madam" : $"Dear: {recipient}";

            var sigName = "VALUATION APPEAL BOARD";
            var sigOrg = "City of Johannesburg";

            var valuationKey = Safe(model.valuation_Key);
            var small7 = TextStyle.Default.FontFamily("Arial").FontSize(7).FontColor(Colors.Grey.Darken2);

            var red7b = TextStyle.Default
                .FontFamily("Arial")
                .FontSize(7)
                .SemiBold()
                .FontColor(Colors.Red.Medium);

            var resolvedRows = BuildResolvedRows(model);

            return Document.Create(doc =>
            {
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginLeft(30);
                    page.MarginRight(30);
                    page.MarginTop(10);
                    page.MarginBottom(10);

                    page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9));

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

                            t.Line(valuationKey).Style(red7b);
                        });

                    page.Content().Column(col =>
                    {
                        col.Spacing(6);

                        if (isPreview)
                        {
                            col.Item()
                                .Border(1)
                                .Background(Colors.Grey.Lighten3)
                                .Padding(6)
                                .AlignCenter()
                                .Text("TEMPLATE PREVIEW (DB / SAMPLE) – FOR ADMIN APPROVAL")
                                .FontFamily("Arial").FontSize(9).SemiBold();
                        }

                        if (File.Exists(headerPath))
                            col.Item().Image(headerPath, ImageScaling.FitWidth);

                        col.Item().PaddingTop(6).Row(r =>
                        {
                            r.RelativeItem().Column(left =>
                            {
                                AddLine(left, model.ADDR1);
                                AddLine(left, model.ADDR2);
                                AddLine(left, model.ADDR3);
                                AddLine(left, model.ADDR4);
                                AddLine(left, model.ADDR5);

                                if (!string.IsNullOrWhiteSpace(model.Email))
                                {
                                    left.Item().PaddingTop(8);
                                    left.Item().Text(Safe(model.Email)).FontFamily("Arial").FontSize(9).SemiBold();
                                }
                            });

                            r.ConstantItem(180).AlignRight()
                                .Text(dateText)
                                .FontFamily("Arial").FontSize(9);
                        });

                        col.Item().PaddingTop(6)
                            .AlignCenter()
                            .Text($"GV2023 {kindShort.ToUpperInvariant()} NOTICE")
                            .FontFamily("Arial").FontSize(12).SemiBold();

                        col.Item().AlignCenter()
                            .Text(title)
                            .FontFamily("Arial").FontSize(9).SemiBold();

                        col.Item().PaddingTop(3).LineHorizontal(1.5f).LineColor(Colors.Grey.Darken2);

                        col.Item().PaddingTop(3).Text(greeting).FontFamily("Arial").FontSize(9).Bold();

                        col.Item().PaddingTop(3).Text(
                            "With reference to the above matter, I wish to advise that the Valuation Appeal Board for the property description below:"
                        ).FontFamily("Arial").FontSize(9).Justify();

                        col.Item().PaddingTop(1).Text(t =>
                        {
                            t.Span("Property Description: ").FontFamily("Arial").FontSize(9).SemiBold();
                            t.Span(Safe(model.Property_desc)).FontFamily("Arial").FontSize(9);
                        });

                        col.Item().PaddingTop(1).Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn();
                                c.RelativeColumn();
                                c.RelativeColumn();
                                c.RelativeColumn();
                                c.RelativeColumn();
                                if (!isReview) c.RelativeColumn();
                            });

                            HeaderCell(t, "SUBURB");
                            HeaderCell(t, "ERF NUMBER");
                            HeaderCell(t, "PORTION");
                            HeaderCell(t, "RE");
                            HeaderCell(t, "OBJECTION NO");
                            if (!isReview) HeaderCell(t, "APPEAL NO");

                            BodyCell(t, model.Town);
                            BodyCell(t, model.ERF);
                            BodyCell(t, model.PTN);
                            BodyCell(t, model.RE);
                            BodyCell(t, model.Objection_No);
                            if (!isReview) BodyCell(t, model.Appeal_No);
                        });

                        col.Item().PaddingTop(5).Text("Resolved inter alia as follows:")
                            .FontFamily("Arial").FontSize(9).SemiBold();

                        col.Item().PaddingTop(3).Element(e => BuildResolvedTable(e, model));

                        col.Item().PaddingTop(10).Column(points =>
                        {
                            points.Item().Text("Please note the following:")
                                .FontFamily("Arial").FontSize(9).SemiBold();

                            Bullet(points, "The decision will be adjusted accordingly to the implementation date being 1 July 2023.");
                            Bullet(points,
                                "The decision will reflect on your account within 30 days. Any adjustments to the account, if applicable, " +
                                "will be made by the Rates and Taxes Department in due course.");
                            Bullet(points,
                                "If you feel aggrieved by the above decision, you are entitled to take the matter on review to the " +
                                "High Court of South Africa at your own cost.");

                            points.Item().PaddingTop(12).Text("Regards,")
                                .FontFamily("Arial").FontSize(9).SemiBold();
                        });

                        col.Item().PaddingTop(14).Column(sig =>
                        {
                            sig.Item().Text(sigName).FontFamily("Arial").FontSize(9).SemiBold();
                            sig.Item().Text(sigOrg).FontFamily("Arial").FontSize(9);
                        });
                    });
                });
            }).GeneratePdf();



            static void AddLine(ColumnDescriptor col, string? line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    col.Item().Text(line.Trim()).FontFamily("Arial").FontSize(9);
            }

            static IContainer CellBase(IContainer c) =>
                c.Border(1).PaddingVertical(4).PaddingHorizontal(6);

            static IContainer BlueHeaderCell(IContainer c) =>
                c.Border(1)
                 .Background(Color.FromRGB(70, 130, 180))
                 .PaddingVertical(4)
                 .PaddingHorizontal(6);

            static void HeaderCell(TableDescriptor t, string text)
            {
                t.Cell().Element(BlueHeaderCell)
                    .Text(text)
                    .FontFamily("Arial")
                    .FontSize(9)
                    .SemiBold()
                    .FontColor(Colors.White);
            }

            static void BodyCell(TableDescriptor t, string? text)
            {
                t.Cell().Element(CellBase)
                    .Text((text ?? "").Trim())
                    .FontFamily("Arial")
                    .FontSize(9);
            }

            static void Bullet(ColumnDescriptor points, string text)
            {
                points.Item().PaddingTop(6).Row(r =>
                {
                    r.ConstantItem(12).Text("•").FontFamily("Arial").FontSize(9);
                    r.RelativeItem().Text(text).FontFamily("Arial").FontSize(9);
                });
            }

            static List<(string Category, string Extent, string MarketValue)> BuildResolvedRows(AppealDecisionRow m)
            {
                var rows = new List<(string, string, string)>();

                void Add(string? cat, object? extent, object? mv)
                {
                    var c = (cat ?? "").Trim();
                    var e = FormatExtent(extent);
                    var v = FormatRand(mv);

                    if (string.IsNullOrWhiteSpace(c) &&
                        string.IsNullOrWhiteSpace(e) &&
                        string.IsNullOrWhiteSpace(v))
                        return;

                    rows.Add((c, e, v));
                }

                Add(m.App_Category, m.App_Extent, m.App_Market_Value);
                Add(m.App_Category2, m.App_Extent2, m.App_Market_Value2);
                Add(m.App_Category3, m.App_Extent3, m.App_Market_Value3);

                return rows;
            }

            static string FormatExtent(object? value)
            {
                if (value is null) return "";

                if (value is decimal dec)
                {
                    if (dec == Math.Truncate(dec))
                        return dec.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ");

                    return dec.ToString("N2", CultureInfo.InvariantCulture).Replace(",", " ");
                }

                if (value is double dbl)
                {
                    var d = Convert.ToDecimal(dbl);
                    if (d == Math.Truncate(d))
                        return d.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ");

                    return d.ToString("N2", CultureInfo.InvariantCulture).Replace(",", " ");
                }

                if (value is int i)
                    return i.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ");

                if (value is long l)
                    return l.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ");

                var raw = value.ToString()?.Trim() ?? "";
                if (raw.Length == 0) return "";

                raw = raw.Replace(",", "").Trim();

                if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                {
                    if (parsed == Math.Truncate(parsed))
                        return parsed.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ");

                    return parsed.ToString("N2", CultureInfo.InvariantCulture).Replace(",", " ");
                }

                return value.ToString()?.Trim() ?? "";
            }

            static string FormatRand(object? value)
            {
                if (value is null) return "";

                if (value is decimal dec)
                    return "R " + dec.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

                if (value is double dbl)
                    return "R " + Convert.ToDecimal(dbl).ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

                if (value is int i)
                    return "R " + i.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

                if (value is long l)
                    return "R " + l.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

                var raw = value.ToString()?.Trim() ?? "";
                if (raw.Length == 0) return "";

                raw = raw.Replace("R", "", StringComparison.OrdinalIgnoreCase)
                         .Replace(",", "")
                         .Trim();

                raw = new string(raw.Where(ch => char.IsDigit(ch) || ch == '.' || ch == '-').ToArray());

                if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
                    return "R " + amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

                return value.ToString()?.Trim() ?? "";
            }

        }
        private static void BuildResolvedTable(IContainer container, AppealDecisionRow model)
        {
            var rows = new List<(string Category, string Extent, string MarketValue)>();

            void AddRow(string? category, object? extent, object? marketValue)
            {
                var cat = string.IsNullOrWhiteSpace(category) ? "" : category.Trim();
                var ext = FormatExtent(extent);
                var mv = FormatRand(marketValue);

                if (string.IsNullOrWhiteSpace(cat) &&
                    string.IsNullOrWhiteSpace(ext) &&
                    string.IsNullOrWhiteSpace(mv))
                    return;

                rows.Add((cat, ext, mv));
            }

            // Main row
            AddRow(model.App_Category, model.App_Extent, model.App_Market_Value);

            // Split rows
            AddRow(model.App_Category2, model.App_Extent2, model.App_Market_Value2);
            AddRow(model.App_Category3, model.App_Extent3, model.App_Market_Value3);

            container.Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                });

                t.Header(h =>
                {
                    h.Cell().Element(HeaderCellBlue).Text("Property Category")
                        .FontFamily("Arial").FontSize(9).SemiBold().FontColor(Colors.White);

                    h.Cell().Element(HeaderCellBlue).Text("Area/m²")
                        .FontFamily("Arial").FontSize(9).SemiBold().FontColor(Colors.White);

                    h.Cell().Element(HeaderCellBlue).Text("Market Value")
                        .FontFamily("Arial").FontSize(9).SemiBold().FontColor(Colors.White);
                });

                if (rows.Count == 0)
                {
                    BodyCell(t, "");
                    BodyCell(t, "");
                    BodyCell(t, "");
                }
                else
                {
                    foreach (var row in rows)
                    {
                        BodyCell(t, row.Category);
                        BodyCell(t, row.Extent);
                        BodyCell(t, row.MarketValue);
                    }
                }
            });

            static IContainer HeaderCellBlue(IContainer c) =>
                c.Border(1)
                 .Background(Color.FromRGB(70, 130, 180))
                 .PaddingVertical(4)
                 .PaddingHorizontal(6);

            static IContainer BodyCellBase(IContainer c) =>
                c.Border(1)
                 .PaddingVertical(4)
                 .PaddingHorizontal(6);

            static void BodyCell(TableDescriptor t, string? text)
            {
                t.Cell().Element(BodyCellBase)
                    .Text((text ?? "").Trim())
                    .FontFamily("Arial")
                    .FontSize(9);
            }
        }
        private static string FormatRand(object? value)
        {
            if (value is null)
                return "";

            if (value is decimal dec)
                return "R " + dec.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

            if (value is double dbl)
                return "R " + Convert.ToDecimal(dbl).ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

            if (value is float flt)
                return "R " + Convert.ToDecimal(flt).ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

            if (value is int i)
                return "R " + i.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

            if (value is long l)
                return "R " + l.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

            var raw = value.ToString()?.Trim() ?? "";
            if (raw.Length == 0)
                return "";

            raw = raw.Replace("R", "", StringComparison.OrdinalIgnoreCase)
                     .Replace(",", "")
                     .Trim();

            raw = new string(raw.Where(ch => char.IsDigit(ch) || ch == '.' || ch == '-').ToArray());

            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
                return "R " + amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

            return value.ToString()?.Trim() ?? "";
        }

        private static string FormatExtent(object? value)
        {
            if (value is null)
                return "";

            if (value is decimal dec)
            {
                if (dec == Math.Truncate(dec))
                    return dec.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ");

                return dec.ToString("N2", CultureInfo.InvariantCulture).Replace(",", " ");
            }

            if (value is double dbl)
            {
                var d = Convert.ToDecimal(dbl);
                if (d == Math.Truncate(d))
                    return d.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ");

                return d.ToString("N2", CultureInfo.InvariantCulture).Replace(",", " ");
            }

            if (value is float flt)
            {
                var f = Convert.ToDecimal(flt);
                if (f == Math.Truncate(f))
                    return f.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ");

                return f.ToString("N2", CultureInfo.InvariantCulture).Replace(",", " ");
            }

            if (value is int i)
                return i.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ");

            if (value is long l)
                return l.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ");

            var raw = value.ToString()?.Trim() ?? "";
            if (raw.Length == 0)
                return "";

            raw = raw.Replace(",", "").Trim();

            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                if (parsed == Math.Truncate(parsed))
                    return parsed.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ");

                return parsed.ToString("N2", CultureInfo.InvariantCulture).Replace(",", " ");
            }

            return value.ToString()?.Trim() ?? "";
        }
    }
}