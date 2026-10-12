using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Helper;
using GV23_Notice.Helpers;

namespace GV23_Notice.Tests.Helpers
{
    public class ValuationPeriodCatalogTests
    {
        [Theory]
        [InlineData("GV23")]
        [InlineData("gv23")]
        [InlineData(" GV23 ")]
        public void Get_finds_period_ignoring_case_and_spaces(string code)
        {
            var period = ValuationPeriodCatalog.Get(code);

            Assert.NotNull(period);
            Assert.Equal(new DateTime(2023, 7, 1), period!.Start);
            Assert.Equal(new DateTime(2027, 6, 30), period.End);
        }

        [Fact]
        public void Get_returns_null_for_unknown_code()
        {
            Assert.Null(ValuationPeriodCatalog.Get("GV99"));
        }

        [Fact]
        public void BuildFinancialYears_returns_each_1_July_to_30_June_inside_the_period()
        {
            var years = ValuationPeriodCatalog.BuildFinancialYears(ValuationPeriodCatalog.Get("GV23")!);

            Assert.Equal(4, years.Count);
            Assert.Equal(new DateTime(2023, 7, 1), years[0].start);
            Assert.Equal(new DateTime(2024, 6, 30), years[0].end);
            Assert.Equal(new DateTime(2026, 7, 1), years[^1].start);
            Assert.Equal(new DateTime(2027, 6, 30), years[^1].end);
            Assert.All(years, y => Assert.Contains(y.start.Year.ToString(), y.text));
        }
    }

    public class AddressDisplayHelperTests
    {
        [Fact]
        public void Removes_owner_name_and_joins_street_number_with_street_name()
        {
            var a = AddressDisplayHelper.Format("Jan Smit", "5", "Lynx Road", "Rosebank", "2196");

            Assert.Equal("5 LYNX ROAD", a.Addr1);
            Assert.Equal("ROSEBANK", a.Addr2);
            Assert.Equal("2196", a.Addr3);
            Assert.Equal("", a.Addr4);
            Assert.Equal("", a.Addr5);
        }

        [Fact]
        public void Keeps_po_box_lines_as_they_are()
        {
            var a = AddressDisplayHelper.Format("PO BOX 71121", "Johannesburg", "2000", null, null);

            Assert.Equal("PO BOX 71121", a.Addr1);
            Assert.Equal("JOHANNESBURG", a.Addr2);
            Assert.Equal("2000", a.Addr3);
        }

        [Fact]
        public void Drops_placeholder_values()
        {
            var a = AddressDisplayHelper.Format("NULL", "N/A", "-", "0", "  ");

            Assert.Equal("", a.Addr1);
            Assert.Equal("", a.Addr2);
        }

        [Fact]
        public void Drops_an_email_address_in_the_first_line()
        {
            var a = AddressDisplayHelper.Format("owner@example.com", "12 Main Road", "Braamfontein", null, null);

            Assert.Equal("12 MAIN ROAD", a.Addr1);
            Assert.Equal("BRAAMFONTEIN", a.Addr2);
        }
    }

    public class EmailCopyNameHelperTests
    {
        private static readonly DateTime Stamp = new(2026, 10, 9, 15, 30, 0);

        [Fact]
        public void S49_uses_the_property_description_and_email()
        {
            var name = EmailCopyNameHelper.BuildEmlFileName(
                "S49", null, null, "ERF 12 ROSEBANK", "owner@example.com", Stamp);

            Assert.Equal("email_ERF_12_ROSEBANK_owner@example.com_20261009_153000.eml", name);
        }

        [Fact]
        public void Other_notices_prefer_the_objection_number()
        {
            var name = EmailCopyNameHelper.BuildEmlFileName(
                "S51", "OBJ-1/2026", "APP-9", "ERF 12", "a@b.co.za", Stamp);

            Assert.StartsWith("email_OBJ-1_2026_a@b.co.za_", name);
        }

        [Fact]
        public void Falls_back_to_Notice_and_NoEmail()
        {
            var name = EmailCopyNameHelper.BuildEmlFileName("S51", null, null, null, null, Stamp);

            Assert.Equal("email_Notice_NoEmail_20261009_153000.eml", name);
        }

        [Fact]
        public void CleanFilePart_makes_a_safe_file_name()
        {
            Assert.Equal("a_b_c", EmailCopyNameHelper.CleanFilePart(" a  b//c "));
            Assert.Equal("", EmailCopyNameHelper.CleanFilePart("   "));
        }
    }

    public class ExtentDisplayHelperTests
    {
        [Theory]
        [InlineData("2330.0000", "2330")]
        [InlineData("17835.5000", "17835.5")]
        [InlineData("17835.2500", "17835.25")]
        [InlineData("2330,0000", "2330")]      // decimal comma (was 23300000 before the fix)
        [InlineData("1,250,000", "1250000")]   // thousands separators
        [InlineData("", "")]
        [InlineData("abc", "abc")]
        public void Formats_strings_without_trailing_zeros(string input, string expected)
        {
            Assert.Equal(expected, ExtentDisplayHelper.SameAsDb(input));
        }

        [Fact]
        public void Formats_numbers_without_trailing_zeros()
        {
            Assert.Equal("2330", ExtentDisplayHelper.SameAsDb(2330.0000m));
            Assert.Equal("127.5", ExtentDisplayHelper.SameAsDb(127.5d));
            Assert.Equal("", ExtentDisplayHelper.SameAsDb(null));
            Assert.Equal("", ExtentDisplayHelper.SameAsDb(DBNull.Value));
        }
    }

    public class NoticeKindExtensionsTests
    {
        [Theory]
        [InlineData(NoticeKind.S53, true, false)]
        [InlineData(NoticeKind.S53Rev, true, true)]
        [InlineData(NoticeKind.S49, false, false)]
        [InlineData(NoticeKind.S52, false, false)]
        public void Section53_family(NoticeKind notice, bool isFamily, bool isRevised)
        {
            Assert.Equal(isFamily, notice.IsSection53Family());
            Assert.Equal(isRevised, notice.IsRevisedMvd());
        }
    }
}
