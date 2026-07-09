
using GV23_Notice.Domain.Workflow.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace GV23_Notice.Services.ThirdPartyApplications
{
    public sealed class ThirdPartyAppealFormalNoticePdfService : IThirdPartyAppealFormalNoticePdfService
    {
        public byte[] BuildPdf(
            NoticeSettings settings,
            ThirdPartyAppealApplicationNotice notice)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (notice == null)
                throw new ArgumentNullException(nameof(notice));

            var letterDate = notice.LetterDate ?? DateTime.Today;
            var valuationPeriod = !string.IsNullOrWhiteSpace(notice.ValuationPeriod)
                ? notice.ValuationPeriod
                : settings.ValuationPeriodCode ?? "GENERAL VALUATION ROLL 2023";

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(35);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                    page.Header().Column(col =>
                    {
                        col.Item().Text("City of Johannesburg")
                            .Bold()
                            .FontSize(14);

                        col.Item().Text("Finance: Property Branch");
                        col.Item().Text("1st Floor, East Wing, Jorissen Place");
                        col.Item().Text("66 Jorissen Street, Braamfontein");
                        col.Item().Text("PO Box 1450, Johannesburg, South Africa, 2000");
                        col.Item().Text("www.joburg.org.za | Tel +27(0) 11 407 6402 (O)");
                        col.Item().PaddingTop(8).LineHorizontal(1);
                    });

                    page.Content().PaddingTop(12).Column(col =>
                    {
                        col.Spacing(8);

                        col.Item().Text($"TO: {BuildOwnerLine(notice)}").Bold();
                        col.Item().Text($"Cc: {BuildThirdPartyLine(notice)}").Bold();
                        col.Item().Text($"DATE: {letterDate:dd MMMM yyyy}").Bold();

                        col.Item().PaddingTop(8).Text("Dear Property Owner,");

                        col.Item().PaddingTop(8)
                            .Text($"NOTICE TO PROPERTY OWNER OF THIRD-PARTY APPEAL APPLICATION TO THE VALUATION APPEAL BOARD [{valuationPeriod}]")
                            .Bold()
                            .FontSize(10);

                        col.Item().Text(text =>
                        {
                            text.Span("This letter serves to formally notify you that an appeal has been lodged by ");
                            text.Span(Display(notice.ThirdPartyName)).Bold();
                            text.Span(" concerning the municipal property valuation of your property namely, ");
                            text.Span(Display(notice.Property_Description)).Bold();
                            text.Span(".");
                        });

                        col.Item().Text(text =>
                        {
                            text.Span("The appeal was lodged by the third party on ");
                            text.Span(FormatDate(notice.DateAdded)).Bold();
                            text.Span(" in terms of section 54 of the Municipal Property Rates Act, 2004 (as amended), following an objection contemplated in section 50(1)(c) of the Act, which permits a person other than the owner to object to the property valuation.");
                        });

                        col.Item().PaddingTop(8).Text("Property Details").Bold();

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2.2f);
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            HeaderCell(table, "");
                            HeaderCell(table, $"{valuationPeriod} Value");
                            HeaderCell(table, $"Objection Outcome {notice.Objection_No}");
                            HeaderCell(table, $"Appellant Request {notice.Appeal_No}");

                            Row(table, "Property Market Value / Property Valuation",
                                Money(notice.RollMarketValue1),
                                Money(notice.ObjectionOutcomeMarketValue1),
                                Money(notice.AppellantRequestMarketValue1));

                            Row(table, "Category",
                                notice.RollCategory1,
                                notice.ObjectionOutcomeCategory1,
                                notice.AppellantRequestCategory1);

                            Row(table, "Extent",
                                notice.RollExtent1,
                                notice.ObjectionOutcomeExtent1,
                                notice.AppellantRequestExtent1);

                            if (notice.IsMultipurpose)
                            {
                                Row(table, "Property Market Value / Property Valuation 2",
                                    Money(notice.RollMarketValue2),
                                    Money(notice.ObjectionOutcomeMarketValue2),
                                    Money(notice.AppellantRequestMarketValue2));

                                Row(table, "Category 2",
                                    notice.RollCategory2,
                                    notice.ObjectionOutcomeCategory2,
                                    notice.AppellantRequestCategory2);

                                Row(table, "Extent 2",
                                    notice.RollExtent2,
                                    notice.ObjectionOutcomeExtent2,
                                    notice.AppellantRequestExtent2);

                                Row(table, "Property Market Value / Property Valuation 3",
                                    Money(notice.RollMarketValue3),
                                    Money(notice.ObjectionOutcomeMarketValue3),
                                    Money(notice.AppellantRequestMarketValue3));

                                Row(table, "Category 3",
                                    notice.RollCategory3,
                                    notice.ObjectionOutcomeCategory3,
                                    notice.AppellantRequestCategory3);

                                Row(table, "Extent 3",
                                    notice.RollExtent3,
                                    notice.ObjectionOutcomeExtent3,
                                    notice.AppellantRequestExtent3);
                            }
                        });

                        col.Item().PaddingTop(8).Text("Reasons for Appeal by the third party are the following:").Bold();
                        col.Item().Text(Display(notice.AppReason));

                        col.Item().PaddingTop(8).Text("Third-party submission relevant to this appeal application and supporting documents are attached for your consideration.");

                        col.Item().Text(text =>
                        {
                            text.Span("Upon consideration as mentioned above, you are hereby requested to electronically file submissions to the VAB Secretariat within ");
                            text.Span("51 calendar days").Bold();
                            text.Span(". Details of electronic filing are ");
                            text.Span("valuationenquiries@joburg.org.za").Bold();
                            text.Span(" and ");
                            text.Span(Display(notice.AdminEmail)).Bold();
                            text.Span(", alternatively physically file at COJ offices, 1st Floor East Wing, Jorissen Place, 66 Jorissen Street, Braamfontein.");
                        });

                        col.Item().Text(text =>
                        {
                            text.Span("The appeal hearing shall be held on ");
                            text.Span(FormatDate(notice.ScheduleDate ?? notice.HearingDate)).Bold();
                            text.Span(" virtually via Microsoft Teams. Meeting link will be sent to you to join the meeting should you so wish.");
                        });

                        col.Item().Text("Please take note that the hearing shall proceed as scheduled, regardless of your contribution and/or adherence to the above procedure.");

                        col.Item().PaddingTop(8).Text("For enquiries kindly contact valuationenquiries@joburg.org.za");

                        col.Item().PaddingTop(12).Text("Regards,");
                        col.Item().Text("Valuation Appeal Board Secretariat");
                        col.Item().Text("City of Johannesburg");

                        col.Item().PaddingTop(8).Text("Attached: Annexure A").Bold();
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Page ");
                        x.CurrentPageNumber();
                        x.Span(" of ");
                        x.TotalPages();
                    });
                });
            }).GeneratePdf();
        }

        private static void HeaderCell(TableDescriptor table, string value)
        {
            table.Cell()
                .Border(1)
                .Background(Colors.Grey.Lighten2)
                .Padding(4)
                .Text(value)
                .Bold();
        }

        private static void Cell(TableDescriptor table, string? value)
        {
            table.Cell()
                .Border(1)
                .Padding(4)
                .Text(Display(value));
        }

        private static void Row(
            TableDescriptor table,
            string label,
            string? roll,
            string? objection,
            string? appellant)
        {
            Cell(table, label);
            Cell(table, roll);
            Cell(table, objection);
            Cell(table, appellant);
        }

        private static string Display(string? value)
            => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

        private static string FormatDate(DateTime? value)
            => value.HasValue ? value.Value.ToString("dd MMMM yyyy") : "—";

        private static string BuildOwnerLine(ThirdPartyAppealApplicationNotice n)
        {
            var parts = new[]
            {
                n.OwnerName,
                n.OwnerAddress1,
                n.OwnerAddress2,
                n.OwnerAddress3,
                n.OwnerAddress4,
                n.OwnerAddress5
            }
            .Where(x => !string.IsNullOrWhiteSpace(x));

            return string.Join(", ", parts);
        }

        private static string BuildThirdPartyLine(ThirdPartyAppealApplicationNotice n)
        {
            var parts = new[]
            {
                n.ThirdPartyName,
                n.ThirdPartyAddress1,
                n.ThirdPartyAddress2,
                n.ThirdPartyAddress3,
                n.ThirdPartyAddress4,
                n.ThirdPartyAddress5
            }
            .Where(x => !string.IsNullOrWhiteSpace(x));

            return string.Join(", ", parts);
        }

        private static string Money(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "—";

            var cleaned = value.Replace("R", "", StringComparison.OrdinalIgnoreCase)
                .Replace(",", "")
                .Trim();

            if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
                return $"R{amount:N0}";

            return value;
        }
    }
}