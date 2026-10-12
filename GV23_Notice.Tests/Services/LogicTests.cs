using GV23_Notice.Controllers;
using GV23_Notice.Services;
using GV23_Notice.Services.ClaThirdPartyApplications;
using GV23_Notice.Services.Email;
using GV23_Notice.Services.Notices.Section49;
using GV23_Notice.Services.Preview;
using Microsoft.Extensions.Configuration;

namespace GV23_Notice.Tests.Services
{
    public class PreviewParserTests
    {
        [Theory]
        [InlineData(null, PreviewVariant.Default)]
        [InlineData("", PreviewVariant.Default)]
        [InlineData("S52Appeal", PreviewVariant.S52AppealDecision)]
        [InlineData("s52appealdecision", PreviewVariant.S52AppealDecision)]
        [InlineData("Review", PreviewVariant.S52ReviewDecision)]
        [InlineData("InvalidOmission", PreviewVariant.InvalidOmission)]
        [InlineData("Objection", PreviewVariant.InvalidObjection)]
        [InlineData("nonsense", PreviewVariant.Default)]
        public void Variant(string? input, PreviewVariant expected)
        {
            Assert.Equal(expected, WorkflowController.PreviewVariantParser.Parse(input));
        }

        [Theory]
        [InlineData(null, PreviewMode.Single)]
        [InlineData("single", PreviewMode.Single)]
        [InlineData("multi", PreviewMode.EmailMulti)]
        [InlineData("EmailMulti", PreviewMode.EmailMulti)]
        [InlineData("split-pdf", PreviewMode.SplitPdf)]
        [InlineData("whatever", PreviewMode.Single)]
        public void Mode(string? input, PreviewMode expected)
        {
            Assert.Equal(expected, WorkflowController.PreviewModeParser.Parse(input));
        }
    }

    public class ClaThirdPartyDateConfigurationServiceTests
    {
        [Fact]
        public void Representation_period_is_30_calendar_days_from_the_letter_date()
        {
            var result = new ClaThirdPartyDateConfigurationService()
                .Calculate(new DateTime(2026, 10, 9, 14, 30, 0));

            Assert.Equal(new DateTime(2026, 10, 9), result.LetterDate);
            Assert.Equal(new DateTime(2026, 10, 9), result.RepresentationStartDate);
            Assert.Equal(new DateTime(2026, 11, 8), result.RepresentationCloseDate);
        }
    }

    public class S53AppealCloseDateCalculatorTests
    {
        private static Mock<IHolidayService> Holidays(params DateOnly[] holidays)
        {
            var mock = new Mock<IHolidayService>();
            mock.Setup(h => h.IsHolidayAsync(It.IsAny<string?>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? _, DateOnly d, CancellationToken _) => holidays.Contains(d));
            mock.Setup(h => h.GetHolidaysBySettingsAsync(It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HashSet<DateOnly>(holidays));
            return mock;
        }

        [Fact]
        public async Task Adds_45_days_on_a_working_day()
        {
            // 2026-09-01 + 45 = 2026-10-16 (Friday)
            var calc = new S53AppealCloseDateCalculator(Holidays().Object);

            var close = await calc.CalculateAsync("GV23", new DateOnly(2026, 9, 1));

            Assert.Equal(new DateOnly(2026, 10, 16), close);
        }

        [Fact]
        public async Task Moves_past_a_weekend_to_Monday()
        {
            // 2026-10-01 + 45 = 2026-11-15 (Sunday) → Monday 16th
            var calc = new S53AppealCloseDateCalculator(Holidays().Object);

            var close = await calc.CalculateAsync("GV23", new DateOnly(2026, 10, 1));

            Assert.Equal(new DateOnly(2026, 11, 16), close);
        }

        [Fact]
        public async Task Moves_past_a_public_holiday()
        {
            // Monday 16th is a holiday → Tuesday 17th
            var calc = new S53AppealCloseDateCalculator(Holidays(new DateOnly(2026, 11, 16)).Object);

            Assert.Equal(new DateOnly(2026, 11, 17), await calc.CalculateAsync("GV23", new DateOnly(2026, 10, 1)));
            Assert.Equal(new DateOnly(2026, 11, 17), await calc.CalculateBySettingsAsync(1, new DateOnly(2026, 10, 1)));
        }

        [Fact]
        public async Task Weekends_are_never_working_days()
        {
            var calc = new S53AppealCloseDateCalculator(Holidays().Object);

            Assert.False(await calc.IsWorkingDayAsync("GV23", new DateOnly(2026, 10, 10))); // Saturday
            Assert.True(await calc.IsWorkingDayAsync("GV23", new DateOnly(2026, 10, 9)));   // Friday
        }
    }

    /// <summary>Who a notice email goes to (owner / test recipient / roll mailbox). Same rule for every notice type.</summary>
    public class RecipientPlanTests
    {
        private const string Mailbox = "GV23Supp4@joburg.org.za";

        [Fact]
        public void Owner_gets_it_and_roll_mailbox_is_bcc()
        {
            var plan = RollMailbox.Plan(" owner@example.com ", Mailbox, testMode: false, testRecipient: null);

            Assert.Equal("owner@example.com", plan.To);
            Assert.Equal(Mailbox, plan.Bcc);
            Assert.False(plan.MailboxOnly);
            Assert.Equal("owner@example.com", plan.OwnerEmail);
        }

        [Fact]
        public void No_owner_email_goes_to_the_roll_mailbox_only()
        {
            var plan = RollMailbox.Plan(null, Mailbox, testMode: false, testRecipient: null);

            Assert.Equal(Mailbox, plan.To);
            Assert.Null(plan.Bcc);
            Assert.True(plan.MailboxOnly);
            Assert.Null(plan.OwnerEmail);
        }

        [Fact]
        public void Test_mode_redirects_the_owner_copy_but_not_the_mailbox()
        {
            var plan = RollMailbox.Plan("owner@example.com", Mailbox, testMode: true, testRecipient: "tester@joburg.org.za");

            Assert.Equal("tester@joburg.org.za", plan.To);
            Assert.Equal(Mailbox, plan.Bcc);
            Assert.Equal("owner@example.com", plan.OwnerEmail);
        }

        [Fact]
        public void Test_mode_with_no_owner_email_still_uses_the_real_mailbox()
        {
            var plan = RollMailbox.Plan("", Mailbox, testMode: true, testRecipient: "tester@joburg.org.za");

            Assert.Equal(Mailbox, plan.To);
            Assert.True(plan.MailboxOnly);
        }

        [Fact]
        public void No_mailbox_configured_means_no_bcc()
        {
            var plan = RollMailbox.Plan("owner@example.com", null, testMode: false, testRecipient: null);

            Assert.Equal("owner@example.com", plan.To);
            Assert.Null(plan.Bcc);
        }

        [Fact]
        public void Mailbox_is_not_copied_to_itself()
        {
            var plan = RollMailbox.Plan(Mailbox.ToLowerInvariant(), Mailbox, testMode: false, testRecipient: null);

            Assert.Null(plan.Bcc);
        }

        [Fact]
        public void No_owner_and_no_mailbox_is_an_error()
        {
            Assert.Throws<InvalidOperationException>(() =>
                RollMailbox.Plan(" ", null, testMode: false, testRecipient: null, reference: "RunLog 7"));
        }

        [Fact]
        public void Test_mode_without_a_test_recipient_is_an_error()
        {
            Assert.Throws<InvalidOperationException>(() =>
                RollMailbox.Plan("owner@example.com", Mailbox, testMode: true, testRecipient: ""));
        }

        [Fact]
        public void Error_names_the_notice_reference()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                RollMailbox.Plan(null, null, testMode: false, testRecipient: null, reference: "APP-123"));

            Assert.Contains("APP-123", ex.Message);
        }

        [Fact]
        public void Apply_puts_the_owner_in_to_and_the_mailbox_in_bcc()
        {
            using var message = new System.Net.Mail.MailMessage();

            RollMailbox.Apply(message, RollMailbox.Plan("owner@example.com", Mailbox, false, null));

            Assert.Equal("owner@example.com", Assert.Single(message.To).Address);
            Assert.Equal(Mailbox, Assert.Single(message.Bcc).Address);
        }

        [Fact]
        public void Apply_mailbox_only_has_no_bcc()
        {
            using var message = new System.Net.Mail.MailMessage();

            RollMailbox.Apply(message, RollMailbox.Plan(null, Mailbox, false, null));

            Assert.Equal(Mailbox, Assert.Single(message.To).Address);
            Assert.Empty(message.Bcc);
        }
    }

    /// <summary>Where the roll mailbox comes from (appsettings RollDb:Sources).</summary>
    public class RollMailboxConfigTests
    {
        private const string Mailbox = "GV23Supp4@joburg.org.za";

        private static IConfiguration Config(params (string Key, string? Value)[] values) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
                .Build();

        [Fact]
        public void Reads_the_roll_level_setting()
        {
            var config = Config(("RollDb:Sources:Objection_Supp4:RollMailbox", Mailbox));

            Assert.Equal(Mailbox, RollMailbox.FromConfig(config, "Objection_Supp4"));
        }

        [Fact]
        public void Falls_back_to_the_older_section_49_setting()
        {
            var config = Config(("RollDb:Sources:Objection_Supp4:Section49:RollMailbox", Mailbox));

            Assert.Equal(Mailbox, RollMailbox.FromConfig(config, " Objection_Supp4 "));
        }

        [Fact]
        public void Other_rolls_get_no_copy()
        {
            var config = Config(("RollDb:Sources:Objection_Supp4:RollMailbox", Mailbox));

            Assert.Null(RollMailbox.FromConfig(config, "Objection"));
            Assert.Null(RollMailbox.FromConfig(config, null));
            Assert.Null(RollMailbox.FromConfig(config, ""));
        }

        [Fact]
        public void Bound_options_use_the_roll_level_setting_first()
        {
            var source = new GV23_Notice.Domain.Rolls.RollSourceOptions
            {
                RollMailbox = " roll@joburg.org.za ",
                Section49 = new GV23_Notice.Domain.Rolls.RollSection49Options { RollMailbox = "old@joburg.org.za" }
            };

            Assert.Equal("roll@joburg.org.za", RollMailbox.FromSource(source));

            source.RollMailbox = "";
            Assert.Equal("old@joburg.org.za", RollMailbox.FromSource(source));

            source.Section49.RollMailbox = "";
            Assert.Null(RollMailbox.FromSource(source));
            Assert.Null(RollMailbox.FromSource(null));
        }
    }

    /// <summary>Address block at the top of the Section 49 letter.</summary>
    public class Section49PostalLinesTests
    {
        [Fact]
        public void Uses_the_postal_address_when_there_is_one()
        {
            var lines = Section49PdfBuilder.BuildPostalLines(new Section49PdfData
            {
                Addr1 = "J SMITH",
                Addr2 = "PO BOX 1",
                Addr3 = "",
                Addr4 = "JOHANNESBURG",
                Addr5 = "2000",
                PhysicalAddress = "2 GOLDING STREET"
            });

            Assert.Equal(new[] { "J SMITH", "PO BOX 1", "JOHANNESBURG", "2000" }, lines);
        }

        [Fact]
        public void No_postal_address_uses_the_owner_at_the_street_address()
        {
            var lines = Section49PdfBuilder.BuildPostalLines(new Section49PdfData
            {
                PropertyDesc = "ERF 500 ROSEBANK",
                PhysicalAddress = "2 Golding Street"
            });

            Assert.Equal(new[] { "THE OWNER", "2 GOLDING STREET", "JOHANNESBURG" }, lines);
        }

        [Fact]
        public void Comma_separated_street_address_goes_on_separate_lines()
        {
            var lines = Section49PdfBuilder.BuildPostalLines(new Section49PdfData
            {
                PhysicalAddress = "2 Golding Street, Rosebank, Johannesburg"
            });

            Assert.Equal(new[] { "THE OWNER", "2 GOLDING STREET", "ROSEBANK", "JOHANNESBURG" }, lines);
        }

        [Fact]
        public void Uses_LisStreetAddress_when_PhysicalAddress_is_blank()
        {
            var lines = Section49PdfBuilder.BuildPostalLines(new Section49PdfData
            {
                PhysicalAddress = "",
                LisStreetAddress = "7 Main Road"
            });

            Assert.Equal(new[] { "THE OWNER", "7 MAIN ROAD", "JOHANNESBURG" }, lines);
        }

        [Fact]
        public void No_address_at_all_falls_back_to_the_property_description()
        {
            var lines = Section49PdfBuilder.BuildPostalLines(new Section49PdfData
            {
                PropertyDesc = "Erf 500 Rosebank"
            });

            Assert.Equal(new[] { "THE OWNER", "ERF 500 ROSEBANK", "JOHANNESBURG" }, lines);
        }

        [Fact]
        public void Address_block_is_never_more_than_5_lines()
        {
            var lines = Section49PdfBuilder.BuildPostalLines(new Section49PdfData
            {
                PhysicalAddress = "Unit 1, Block A, 2 Long Street, Suburb, City, Province"
            });

            Assert.Equal(5, lines.Count);
            Assert.Equal("THE OWNER", lines[0]);
            Assert.Equal("JOHANNESBURG", lines[^1]);
        }
    }
}
