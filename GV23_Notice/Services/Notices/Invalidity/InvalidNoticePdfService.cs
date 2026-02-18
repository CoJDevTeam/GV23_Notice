using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace GV23_Notice.Services.Notices.Invalidity
{
    public sealed class InvalidNoticePdfService : IInvalidNoticePdfService
    {
        public byte[] BuildNotice(InvalidNoticePdfData data, InvalidNoticePdfContext ctx)
            => BuildPdf(data, ctx, isPreview: false);

        public byte[] BuildPreview(InvalidNoticePreviewData preview)
        {
            var ctx = new InvalidNoticePdfContext
            {
                HeaderImagePath = preview.HeaderImagePath,
                LetterDate = preview.LetterDate,
                EnquiriesLine = preview.EnquiriesLine
            };

            var data = new InvalidNoticePdfData
            {
                Kind = preview.Kind,
                ObjectionNo = preview.ObjectionNo,
                PropertyDescription = preview.PropertyDescription,
                RecipientName = preview.RecipientName,
                RecipientAddress = preview.RecipientAddress
            };

            return BuildPdf(data, ctx, isPreview: true);
        }

        private static byte[] BuildPdf(InvalidNoticePdfData data, InvalidNoticePdfContext ctx, bool isPreview)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (ctx is null) throw new ArgumentNullException(nameof(ctx));

            var model = new Model(data, ctx);

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginLeft(30);
                    page.MarginRight(30);
                    page.MarginTop(10);
                    page.MarginBottom(10);

                    page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9));

                    page.Content().Column(col =>
                    {
                        // ✅ Preview banner (same pattern)
                        if (isPreview)
                        {
                            col.Item()
                               .AlignCenter()
                               .Text("TEMPLATE PREVIEW (DUMMY DATA)")
                               .SemiBold()
                               .FontSize(10);

                            col.Item().PaddingTop(6);
                        }

                        // 1) Header image
                        if (!string.IsNullOrWhiteSpace(model.HeaderImagePath) && File.Exists(model.HeaderImagePath))
                        {
                            col.Item().Height(95).AlignCenter().Image(model.HeaderImagePath, ImageScaling.FitArea);
                            col.Item().PaddingTop(5);
                        }

                        // 2) Address (left) + Date (right)
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(left =>
                            {
                                // ✅ Address lines from RecipientAddress (or XXXX lines)
                                foreach (var line in BuildAddressLines(model.RecipientAddress))
                                    left.Item().Text(line).FontSize(10);
                            });

                            row.ConstantItem(180).AlignRight().Column(right =>
                            {
                                right.Item().Text(model.LetterDate.ToString("dd MMMM yyyy")).FontSize(9);
                            });
                        });

                        col.Item().PaddingTop(10);

                        // 3) Title block
                        col.Item().AlignCenter().Text("CITY OF JOHANNESBURG")
                            .FontSize(12).SemiBold();

                        col.Item().AlignCenter().Text(
                                model.Kind == InvalidNoticeKind.InvalidOmission
                                    ? "INVALID OMMISSION OBJECTION"
                                    : "INVALID OBJECTION"
                            )
                            .FontSize(10)
                            .SemiBold();

                        col.Item().PaddingTop(6).LineHorizontal(1.5f);
                        col.Item().PaddingTop(10);

                        // 4) Property Description
                        col.Item().Text("Property Description:").SemiBold();
                        col.Item().PaddingTop(2).Text(model.PropertyDescription);

                        col.Item().PaddingTop(10);

                        // 5) Body (your logic)
                        col.Item().Text(t =>
                        {
                            t.Span("Objection number: ");
                            t.Span(model.ObjectionNo).SemiBold();
                            t.Span("\n\n");

                            t.Span("Dear ").SemiBold();
                            t.Span(model.RecipientName).SemiBold();
                            t.Span("\n\n");

                            t.Span(model.Kind == InvalidNoticeKind.InvalidObjection
                                ? "Please be advised that the objection submitted cannot be considered. The records indicate that the objection was lodged against a property description/property that does not exist on the official applicable property register."
                                : "Please be advised that the objection submitted cannot be considered. The records indicate that the objection was lodged against the incorrect property description.");
                        });

                        col.Item().PaddingTop(10)
                            .Text("As a result, a section 53 notice will not be issued, as the objection is not valid for the reasons stated above.")
                            .Justify()
                            .SemiBold();

                        col.Item().PaddingTop(12);

                        // 6) Bullet points
                        Bullet(col,
                            "Should you wish to submit an objection in line with the applicable property details, please ensure that the correct property description is used.");

                        col.Item().PaddingTop(10);
                        col.Item().Text(model.EnquiriesLine).FontSize(9);

                        col.Item().PaddingTop(14);

                        // 7) Signature block
                        col.Item().Text("S. Faiaz").SemiBold();
                        col.Item().Text("Municipal Valuer");
                    });

                    // Footer
                    page.Footer().AlignCenter().Column(col =>
                    {
                        col.Item()
                            .PaddingTop(6).AlignCenter()
                            .Text("_______________________________________________")
                            .FontSize(7)
                            .FontColor(Colors.Grey.Darken2);

                        col.Item()
                            .Text("This is an official document generated by the City of Johannesburg Valuation Services Department")
                            .FontSize(7)
                            .AlignCenter()
                            .FontColor(Colors.Grey.Darken2);

                        col.Item()
                            .Text($"Generated on: {model.LetterDate:dd MMMM yyyy}")
                            .FontSize(7)
                            .AlignCenter()
                            .FontColor(Colors.Grey.Darken2);
                    });
                });
            }).GeneratePdf();
        }

        private static IReadOnlyList<string> BuildAddressLines(string? addressMultiline)
        {
            var lines = (addressMultiline ?? "")
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(x => (x ?? "").Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            // If address present: pad to 4 lines, keep to 5 max
            if (lines.Count > 0)
            {
                while (lines.Count < 4) lines.Add("");
                return lines.Take(5).ToList();
            }

            // No address -> XXXX lines
            return new List<string> { "XXXX", "XXXX", "XXXX", "XXXX" };
        }

        private static void Bullet(ColumnDescriptor col, string text)
        {
            col.Item().Row(row =>
            {
                row.ConstantItem(12).Text("•").FontSize(10);
                row.RelativeItem().Text(text).Justify();
            });
            col.Item().PaddingTop(4);
        }

        private sealed record Model(InvalidNoticePdfData Data, InvalidNoticePdfContext Ctx)
        {
            public string HeaderImagePath => Ctx.HeaderImagePath;
            public DateTime LetterDate => Ctx.LetterDate;
            public string EnquiriesLine => Ctx.EnquiriesLine;

            public InvalidNoticeKind Kind => Data.Kind;

            public string ObjectionNo => Data.ObjectionNo ?? "";
            public string PropertyDescription => Data.PropertyDescription ?? "";

            public string RecipientAddress => Data.RecipientAddress ?? "";

            public string RecipientName =>
                string.IsNullOrWhiteSpace(Data.RecipientName) ? "Sir/Madam" : Data.RecipientName.Trim();
        }
    }
}

