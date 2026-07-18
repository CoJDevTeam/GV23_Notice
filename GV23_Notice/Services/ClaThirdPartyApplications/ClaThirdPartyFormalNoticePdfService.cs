using GV23_Notice.Domain.Workflow.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace GV23_Notice.Services.ClaThirdPartyApplications
{
    public sealed class ClaThirdPartyFormalNoticePdfService
        : IClaThirdPartyFormalNoticePdfService
    {
        private readonly IWebHostEnvironment _env;

        public ClaThirdPartyFormalNoticePdfService(
            IWebHostEnvironment env)
        {
            _env = env;
        }

        public byte[] BuildPdf(
            NoticeSettings settings,
            ClaThirdPartyApplicationNotice notice)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(notice);

            QuestPDF.Settings.License =
                LicenseType.Community;

            var letterDate =
                notice.LetterDate ??
                settings.LetterDate.Date;

            var closingDate =
                notice.RepresentationCloseDate ??
                settings.ObjectionEndDate ??
                letterDate.AddDays(30);

            var rollDisplayName = FirstNonEmpty(
                notice.ValuationPeriod,
                settings.RollName,
                settings.ValuationPeriodCode,
                "General Valuation Roll 2023");

            var headerPath =
                ResolveHeaderImagePath();

            var title12 = TextStyle.Default
                .FontFamily("Arial")
                .FontSize(12)
                .SemiBold();

            var body9 = TextStyle.Default
                .FontFamily("Arial")
                .FontSize(9);

            var body9b = TextStyle.Default
                .FontFamily("Arial")
                .FontSize(9)
                .SemiBold();

            var body10b = TextStyle.Default
                .FontFamily("Arial")
                .FontSize(10)
                .SemiBold();

            var value9 = TextStyle.Default
                .FontFamily("Arial")
                .FontSize(9)
                .SemiBold()
                .FontColor(Colors.Black);

            var small7 = TextStyle.Default
                .FontFamily("Arial")
                .FontSize(7)
                .FontColor(Colors.Grey.Darken2);

            var footerRef7 = TextStyle.Default
                .FontFamily("Arial")
                .FontSize(7)
                .SemiBold()
                .FontColor(Colors.Grey.Darken3);

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginLeft(30);
                    page.MarginRight(30);
                    page.MarginTop(8);
                    page.MarginBottom(8);

                    page.DefaultTextStyle(x =>
                        x.FontFamily("Arial")
                            .FontSize(9));

                    BuildFooter(
                        page,
                        notice,
                        letterDate,
                        small7,
                        footerRef7);

                    page.Content().Column(column =>
                    {
                        column.Spacing(5);

                        AddHeaderImage(
                            column,
                            headerPath,
                            body9b);

                        column.Item().PaddingTop(6);

                        BuildAddressSection(
                            column,
                            notice,
                            letterDate,
                            body10b,
                            value9);

                        column.Item()
                            .PaddingTop(10)
                            .Text("Dear Property Owner,")
                            .Style(body9b);

                        BuildHeading(
                            column,
                            rollDisplayName,
                            title12);

                        BuildIntroduction(
                            column,
                            notice,
                            body9,
                            value9);

                        column.Item()
                            .PaddingTop(5)
                            .Element(container =>
                                BuildPropertyDetailsTable(
                                    container,
                                    notice,
                                    rollDisplayName));

                        column.Item()
                            .PaddingTop(7)
                            .Text(
                                "The third-party application for condonation of a late appeal, together with the supporting documentation relevant to the application, is attached for your consideration.")
                            .Style(body9)
                            .Justify();

                        BuildRepresentationParagraph(
                            column,
                            notice,
                            closingDate,
                            body9,
                            body9b,
                            value9);

                        column.Item()
                            .PaddingTop(5)
                            .Text(
                                "Please note that, upon expiry of the 30-day submission period, the Condonation for Late Appeal Committee will consider the application and may determine the matter with or without any representations received.")
                            .Style(body9)
                            .Justify();

                        column.Item()
                            .PaddingTop(5)
                            .Text(text =>
                            {
                                text.Span(
                                        "Should you require any further information regarding this notice, please contact ")
                                    .Style(body9);

                                text.Span(
                                        "valuationenquiries@joburg.org.za")
                                    .Style(value9);

                                text.Span(".")
                                    .Style(body9);
                            });

                        column.Item()
                            .PaddingTop(7)
                            .Text("Regards,")
                            .Style(body9);

                        column.Item()
                            .PaddingTop(14)
                            .Text(
                                "Condonation for Late Appeal Committee")
                            .Style(body9b);
                    });
                });
            }).GeneratePdf();
        }
        private static void BuildFooter(
    PageDescriptor page,
    ClaThirdPartyApplicationNotice notice,
    DateTime letterDate,
    TextStyle small7,
    TextStyle footerRef7)
        {
            page.Footer()
                .PaddingTop(8)
                .AlignCenter()
                .Text(text =>
                {
                    text.Line(
                            "_______________________________________________")
                        .Style(small7);

                    text.Line(
                            "This is an official document generated by the City of Johannesburg Condonation for Late Appeal Committee")
                        .Style(small7);

                    text.Line(
                            $"Generated on: {letterDate:dd MMMM yyyy}")
                        .Style(small7);

                    text.Line(
                            $"CLA number: {Display(notice.ClaNumber)}")
                        .Style(footerRef7);
                });
        }
        private static void BuildAddressSection(
    ColumnDescriptor column,
    ClaThirdPartyApplicationNotice notice,
    DateTime letterDate,
    TextStyle labelStyle,
    TextStyle valueStyle)
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Element(container =>
                        BuildFormalAddress(
                            container,
                            "TO:",
                            BuildOwnerAddressLines(notice),
                            labelStyle,
                            valueStyle));

                    left.Item()
                        .PaddingTop(3)
                        .Element(container =>
                            BuildFormalAddress(
                                container,
                                "Cc:",
                                BuildThirdPartyAddressLines(notice),
                                labelStyle,
                                valueStyle));
                });

                row.ConstantItem(170)
                    .AlignRight()
                    .Text(
                        letterDate.ToString(
                            "dd MMMM yyyy",
                            CultureInfo.GetCultureInfo("en-ZA")))
                    .Style(valueStyle);
            });
        }

        private static void BuildHeading(
    ColumnDescriptor column,
    string rollDisplayName,
    TextStyle titleStyle)
        {
            column.Item()
                .PaddingTop(5)
                .AlignCenter()
                .Text(text =>
                {
                    text.Span(
                            "NOTICE TO PROPERTY OWNER OF A THIRD-PARTY APPLICATION FOR CONDONATION OF A LATE APPEAL - ")
                        .Style(titleStyle);

                    text.Span(
                            rollDisplayName.ToUpperInvariant())
                        .Style(titleStyle);
                });
        }

        private static void BuildIntroduction(
    ColumnDescriptor column,
    ClaThirdPartyApplicationNotice notice,
    TextStyle bodyStyle,
    TextStyle valueStyle)
        {
            column.Item()
                .PaddingTop(6)
                .Text(text =>
                {
                    text.Span(
                            "This letter serves as formal notification that the Condonation for Late Appeal Committee has received an application for condonation of a late appeal submitted by ")
                        .Style(bodyStyle);

                    text.Span(
                            Display(notice.ThirdPartyName))
                        .Style(valueStyle);

                    text.Span(
                            ", acting under delegated authority on behalf of the City of Johannesburg, in respect of the municipal valuation of your property described below.")
                        .Style(bodyStyle);
                });

            column.Item()
                .PaddingTop(5)
                .Text(
                    "The application for condonation of a late appeal was submitted following a third-party objection contemplated in section 50(1)(c) of the Local Government: Municipal Property Rates Act, 2004 (Act No. 6 of 2004), which permits a person other than the owner of a property to lodge an objection against the valuation of that property.")
                .Style(bodyStyle)
                .Justify();
        }

        private static void BuildPropertyDetailsTable(
    IContainer container,
    ClaThirdPartyApplicationNotice notice,
    string rollDisplayName)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(150);
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell()
                        .Element(HeaderCell)
                        .Text("Property Details")
                        .FontFamily("Arial")
                        .FontSize(9)
                        .SemiBold();

                    header.Cell()
                        .Element(HeaderCell)
                        .Text($"{rollDisplayName} Value")
                        .FontFamily("Arial")
                        .FontSize(8.5f)
                        .SemiBold();

                    header.Cell()
                        .Element(HeaderCell)
                        .Column(column =>
                        {
                            column.Spacing(3);

                            column.Item()
                                .Text("Objection Outcome")
                                .FontFamily("Arial")
                                .FontSize(9)
                                .SemiBold();

                            column.Item()
                                .Text(Display(
                                    notice.ObjectionNumber))
                                .FontFamily("Arial")
                                .FontSize(8)
                                .SemiBold();
                        });

                    header.Cell()
                        .Element(HeaderCell)
                        .Column(column =>
                        {
                            column.Spacing(3);

                            column.Item()
                                .Text("Appellant Request")
                                .FontFamily("Arial")
                                .FontSize(9)
                                .SemiBold();

                            column.Item()
                                .Text(Display(
                                    notice.ClaNumber))
                                .FontFamily("Arial")
                                .FontSize(8)
                                .SemiBold();
                        });
                });

                DataRow(
                    table,
                    "Property Market Value/\nProperty Valuation",
                    FormatRand(notice.RollMarketValue1),
                    FormatRand(
                        notice.ObjectionOutcomeMarketValue1),
                    FormatRand(
                        notice.ClaRequestedMarketValue1));

                DataRow(
                    table,
                    "Category",
                    SafeText(notice.RollCategory1),
                    SafeText(
                        notice.ObjectionOutcomeCategory1),
                    SafeText(
                        notice.ClaRequestedCategory1));

                DataRow(
                    table,
                    "Extent",
                    FormatExtent(notice.RollExtent1),
                    FormatExtent(
                        notice.ObjectionOutcomeExtent1),
                    FormatExtent(
                        notice.ClaRequestedExtent1));

                if (notice.IsMultipurpose)
                {
                    EmptyRow(table);

                    DataRow(
                        table,
                        "Property Market Value Split 1",
                        FormatRand(notice.RollMarketValue2),
                        FormatRand(
                            notice.ObjectionOutcomeMarketValue2),
                        FormatRand(
                            notice.ClaRequestedMarketValue2));

                    DataRow(
                        table,
                        "Category Split 1",
                        SafeText(notice.RollCategory2),
                        SafeText(
                            notice.ObjectionOutcomeCategory2),
                        SafeText(
                            notice.ClaRequestedCategory2));

                    DataRow(
                        table,
                        "Extent Split 1",
                        FormatExtent(notice.RollExtent2),
                        FormatExtent(
                            notice.ObjectionOutcomeExtent2),
                        FormatExtent(
                            notice.ClaRequestedExtent2));

                    EmptyRow(table);

                    DataRow(
                        table,
                        "Property Market Value Split 2",
                        FormatRand(notice.RollMarketValue3),
                        FormatRand(
                            notice.ObjectionOutcomeMarketValue3),
                        FormatRand(
                            notice.ClaRequestedMarketValue3));

                    DataRow(
                        table,
                        "Category Split 2",
                        SafeText(notice.RollCategory3),
                        SafeText(
                            notice.ObjectionOutcomeCategory3),
                        SafeText(
                            notice.ClaRequestedCategory3));

                    DataRow(
                        table,
                        "Extent Split 2",
                        FormatExtent(notice.RollExtent3),
                        FormatExtent(
                            notice.ObjectionOutcomeExtent3),
                        FormatExtent(
                            notice.ClaRequestedExtent3));
                }
            });
        }

        private static void DataRow(
    TableDescriptor table,
    string label,
    string? roll,
    string? objection,
    string? claRequest)
        {
            table.Cell()
                .Element(LabelCell)
                .Text(label)
                .FontFamily("Arial")
                .FontSize(9)
                .SemiBold();

            table.Cell()
                .Element(ValueCell)
                .Text(roll ?? "")
                .FontFamily("Arial")
                .FontSize(9);

            table.Cell()
                .Element(ValueCell)
                .Text(objection ?? "")
                .FontFamily("Arial")
                .FontSize(9);

            table.Cell()
                .Element(ValueCell)
                .Text(claRequest ?? "")
                .FontFamily("Arial")
                .FontSize(9);
        }

        private static void EmptyRow(
            TableDescriptor table)
        {
            table.Cell().Element(CellBase).Text("");
            table.Cell().Element(CellBase).Text("");
            table.Cell().Element(CellBase).Text("");
            table.Cell().Element(CellBase).Text("");
        }

        private static void BuildRepresentationParagraph(
    ColumnDescriptor column,
    ClaThirdPartyApplicationNotice notice,
    DateTime closingDate,
    TextStyle bodyStyle,
    TextStyle boldStyle,
    TextStyle valueStyle)
        {
            column.Item()
                .PaddingTop(7)
                .Text(text =>
                {
                    text.Span(
                            "You are hereby invited to submit written representations regarding this application to the Condonation for Late Appeal Committee within ")
                        .Style(bodyStyle);

                    text.Span("30 calendar days")
                        .Style(boldStyle);

                    text.Span(
                            $" from the date of this notice. The representation closing date is {closingDate:dd MMMM yyyy}.")
                        .Style(bodyStyle);
                });

            column.Item()
                .PaddingTop(5)
                .Text(text =>
                {
                    text.Span(
                            "Written submissions may be submitted electronically to ")
                        .Style(bodyStyle);

                    text.Span(
                            "valuationenquiries@joburg.org.za")
                        .Style(valueStyle);

                    if (!string.IsNullOrWhiteSpace(
                            notice.AdminEmail))
                    {
                        text.Span(" or ")
                            .Style(bodyStyle);

                        text.Span(
                                notice.AdminEmail.Trim())
                            .Style(valueStyle);
                    }

                    text.Span(
                            ". Alternatively, submissions may be delivered by hand to City of Johannesburg, 1st Floor, East Wing, Jorissen Place, 66 Jorissen Street, Braamfontein.")
                        .Style(bodyStyle);
                });
        }


        private void AddHeaderImage(
    ColumnDescriptor column,
    string headerPath,
    TextStyle errorStyle)
        {
            if (!string.IsNullOrWhiteSpace(headerPath) &&
                File.Exists(headerPath))
            {
                column.Item()
                    .Image(headerPath, ImageScaling.FitWidth);

                return;
            }

            column.Item()
                .Border(1)
                .Padding(6)
                .Background(Colors.Grey.Lighten4)
                .Text(text =>
                {
                    text.Span("HEADER IMAGE NOT FOUND")
                        .Style(errorStyle);

                    text.Line($"Expected: {headerPath}");

                    text.Line(
                        $"WebRoot: {_env.WebRootPath ?? "(null)"}");
                });
        }

        private string ResolveHeaderImagePath()
        {
            var candidates = new[]
            {
        Path.Combine(
            _env.WebRootPath ?? "",
            "Images",
            "Obj_Header.PNG"),

        Path.Combine(
            _env.WebRootPath ?? "",
            "images",
            "Obj_Header.PNG"),

        Path.Combine(
            _env.WebRootPath ?? "",
            "img",
            "Obj_Header.PNG"),

        Path.Combine(
            _env.WebRootPath ?? "",
            "Images",
            "Obj_Header.png"),

        Path.Combine(
            _env.WebRootPath ?? "",
            "images",
            "Obj_Header.png"),

        Path.Combine(
            _env.WebRootPath ?? "",
            "img",
            "Obj_Header.png")
    };

            return candidates.FirstOrDefault(File.Exists)
                ?? candidates[0];
        }

        private static void BuildFormalAddress(
            IContainer container,
            string label,
            IReadOnlyList<string> lines,
            TextStyle labelStyle,
            TextStyle valueStyle)
        {
            container.Row(row =>
            {
                row.ConstantItem(28)
                    .Text(label)
                    .Style(labelStyle);

                row.RelativeItem()
                    .Column(column =>
                    {
                        if (lines.Count == 0)
                        {
                            column.Item()
                                .Text("—")
                                .Style(valueStyle);

                            return;
                        }

                        foreach (var line in lines)
                        {
                            column.Item()
                                .Text(line)
                                .Style(valueStyle);
                        }
                    });
            });
        }

        private static IReadOnlyList<string>
            BuildOwnerAddressLines(
                ClaThirdPartyApplicationNotice notice)
        {
            return BuildAddressLines(
                notice.OwnerName,
                notice.OwnerAddress1,
                notice.OwnerAddress2,
                notice.OwnerAddress3,
                notice.OwnerAddress4,
                notice.OwnerAddress5);
        }

        private static IReadOnlyList<string>
            BuildThirdPartyAddressLines(
                ClaThirdPartyApplicationNotice notice)
        {
            return BuildAddressLines(
                notice.ThirdPartyName,
                notice.ThirdPartyAddress1,
                notice.ThirdPartyAddress2,
                notice.ThirdPartyAddress3,
                notice.ThirdPartyAddress4,
                notice.ThirdPartyAddress5);
        }

        private static IReadOnlyList<string> BuildAddressLines(
            params string?[] values)
        {
            return values
                .Where(value =>
                    !string.IsNullOrWhiteSpace(value))
                .Select(value =>
                    value!
                        .Trim()
                        .Trim(','))
                .Where(value => value.Length > 0)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string FirstNonEmpty(
            params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return "";
        }

        private static string Display(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "—"
                : value.Trim();
        }

        private static string SafeText(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? ""
                : value.Trim();
        }

        private static string FormatRand(decimal? value)
        {
            if (!value.HasValue)
            {
                return "";
            }

            return "R " +
                   value.Value
                       .ToString(
                           "#,##0",
                           CultureInfo.InvariantCulture)
                       .Replace(",", " ");
        }

        private static string FormatExtent(decimal? value)
        {
            if (!value.HasValue)
            {
                return "";
            }

            var amount = value.Value;

            if (amount == Math.Truncate(amount))
            {
                return amount
                    .ToString(
                        "N0",
                        CultureInfo.InvariantCulture)
                    .Replace(",", " ");
            }

            return amount
                .ToString(
                    "N2",
                    CultureInfo.InvariantCulture)
                .Replace(",", " ");
        }

        private static IContainer HeaderCell(
            IContainer container)
        {
            return container
                .Border(0.8f)
                .BorderColor(Colors.Grey.Darken2)
                .Background(Colors.Grey.Lighten3)
                .PaddingVertical(5)
                .PaddingHorizontal(4);
        }

        private static IContainer LabelCell(
            IContainer container)
        {
            return container
                .Border(0.8f)
                .BorderColor(Colors.Grey.Darken2)
                .Background(Colors.Grey.Lighten4)
                .PaddingVertical(5)
                .PaddingHorizontal(4);
        }

        private static IContainer ValueCell(
            IContainer container)
        {
            return container
                .Border(0.8f)
                .BorderColor(Colors.Grey.Darken2)
                .PaddingVertical(5)
                .PaddingHorizontal(4);
        }

        private static IContainer CellBase(
            IContainer container)
        {
            return container
                .Border(0.8f)
                .BorderColor(Colors.Grey.Darken2)
                .PaddingVertical(5)
                .PaddingHorizontal(4);
        }
    }

}