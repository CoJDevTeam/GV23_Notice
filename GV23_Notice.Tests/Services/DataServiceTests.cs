using GV23_Notice.Domain.Rolls;
using GV23_Notice.Domain.Section49;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Services;
using GV23_Notice.Services.Audit;
using GV23_Notice.Services.Preview;
using GV23_Notice.Services.QA;
using GV23_Notice.Services.Rolls;
using GV23_Notice.Services.SnapShotStep2;
using GV23_Notice.Services.Step3;
using GV23_Notice.Services.Workflow;
using GV23_Notice.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace GV23_Notice.Tests.Services
{
    public class NoticeStep2SnapshotServiceTests
    {
        private readonly TestDb _db = new();

        private Task<int> Approve(int settingsId) =>
            new NoticeStep2SnapshotService(_db.NewContext()).SaveApprovalAsync(
                settingsId, PreviewVariant.Default, PreviewMode.Single, "approver",
                "Subject", "<p>Body</p>", new byte[] { 1, 2, 3 }, "preview.pdf", default);

        [Fact]
        public async Task First_approval_saves_one_snapshot_and_marks_Step2_approved()
        {
            _db.Seed(db => { db.RollRegistry.Add(Make.Supp4Roll()); db.NoticeSettings.Add(Make.Settings(id: 1)); });

            var snapId = await Approve(1);

            using var check = _db.NewContext();
            Assert.True(snapId > 0);
            Assert.Equal(1, await check.NoticeStep2Snapshots.CountAsync());
            var s = await check.NoticeSettings.SingleAsync();
            Assert.True(s.Step2Approved);
            Assert.Equal("approver", s.Step2ApprovedBy);
        }

        [Fact]
        public async Task Approving_twice_does_not_create_a_second_snapshot()
        {
            _db.Seed(db => { db.RollRegistry.Add(Make.Supp4Roll()); db.NoticeSettings.Add(Make.Settings(id: 1)); });

            var first = await Approve(1);
            var second = await Approve(1);

            using var check = _db.NewContext();
            Assert.Equal(first, second);
            Assert.Equal(1, await check.NoticeStep2Snapshots.CountAsync());
        }

        [Fact]
        public async Task Step1_must_be_approved_first()
        {
            _db.Seed(db => { db.RollRegistry.Add(Make.Supp4Roll()); db.NoticeSettings.Add(Make.Settings(id: 1, approved: false)); });

            await Assert.ThrowsAsync<InvalidOperationException>(() => Approve(1));
        }

        [Fact]
        public async Task Correction_after_approval_is_refused()
        {
            _db.Seed(db => { db.RollRegistry.Add(Make.Supp4Roll()); db.NoticeSettings.Add(Make.Settings(id: 1)); });
            await Approve(1);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new NoticeStep2SnapshotService(_db.NewContext()).SaveCorrectionAsync(
                    1, PreviewVariant.Default, PreviewMode.Single, "someone", "wrong date",
                    "", "", Array.Empty<byte>(), "", default));
        }
    }

    public class NoticeSettingsAuditServiceTests
    {
        [Fact]
        public async Task Writes_one_audit_row_with_the_snapshot_as_json()
        {
            var db = new TestDb();
            db.Seed(ctx => ctx.NoticeSettings.Add(Make.Settings(id: 1)));

            await new NoticeSettingsAuditService(db.NewContext())
                .WriteAsync(1, "Step1", "SAVE", "tester", new { Version = 3 }, "note", default);

            using var check = db.NewContext();
            var row = await check.NoticeSettingsAudits.SingleAsync();
            Assert.Equal("SAVE", row.Action);
            Assert.Equal("tester", row.PerformedBy);
            Assert.Contains("\"Version\":3", row.SnapshotJson);
        }
    }

    public class Step3BatchServiceTests
    {
        private readonly TestDb _db = new();
        private static readonly Guid Key = Guid.NewGuid();

        private Step3BatchService Service(int s49BatchSize = 500) =>
            new(_db.NewContext(),
                Mock.Of<INoticeSourceStatusService>(),
                Mock.Of<IS49RollRepository>(),
                Options.Create(new Section49Options { Batch = new Section49BatchOptions { Size = s49BatchSize } }));

        [Fact]
        public async Task First_batch_is_0001()
        {
            _db.Seed(db => { db.RollRegistry.Add(Make.Supp4Roll()); db.NoticeSettings.Add(Make.Settings(id: 1, approvalKey: Key)); });

            Assert.Equal("S49_SUPP 4_0001", await Service().PeekNextBatchNameAsync(1, default));
        }

        [Fact]
        public async Task Next_batch_keeps_the_space_in_the_roll_code_and_follows_the_highest_number()
        {
            // Regression: the dashboard used "S49_SUPP4_" (space removed), never found
            // the real batches and always showed 0001.
            _db.Seed(db =>
            {
                db.RollRegistry.Add(Make.Supp4Roll());
                db.NoticeSettings.Add(Make.Settings(id: 1, approvalKey: Key));
                db.NoticeBatches.Add(Make.Batch(1, 1, Key, "S49_SUPP 4_0003", 10));
                db.NoticeBatches.Add(Make.Batch(2, 1, Key, "S49_SUPP 4_0010", 10));
                db.NoticeBatches.Add(Make.Batch(3, 1, Key, "S49_SUPP 4_0007", 10)); // newest row, lower number
                db.NoticeBatches.Add(Make.Batch(4, 1, Key, "S49_SUPP 4_0099", 10, batchKind: "S49 Bulk")); // not a Step 3 batch
                db.NoticeBatches.Add(Make.Batch(5, 1, Key, "S49_GV23_0050", 10, rollId: 1)); // other roll
            });

            Assert.Equal("S49_SUPP 4_0011", await Service().PeekNextBatchNameAsync(1, default));
        }

        [Fact]
        public async Task Section52_review_and_appeal_decision_have_their_own_numbers()
        {
            _db.Seed(db =>
            {
                db.RollRegistry.Add(Make.Supp4Roll());
                var review = Make.Settings(id: 1, notice: NoticeKind.S52, approvalKey: Key);
                review.IsSection52Review = true;
                db.NoticeSettings.Add(review);
                db.NoticeSettings.Add(Make.Settings(id: 2, notice: NoticeKind.S52, approvalKey: Guid.NewGuid()));
                db.NoticeBatches.Add(Make.Batch(1, 1, Key, "S52_SUPP 4_0004", 5, notice: NoticeKind.S52));
            });

            Assert.Equal("S52_SUPP 4_0005", await Service().PeekNextBatchNameAsync(1, default));
            Assert.Equal("AD_SUPP 4_0001", await Service().PeekNextBatchNameAsync(2, default));
        }

        [Fact]
        public void Records_per_batch_comes_from_config_for_S49_only()
        {
            var svc = Service(s49BatchSize: 10);

            Assert.Equal(10, svc.GetRecordsPerBatch(NoticeKind.S49));
            Assert.Equal(500, svc.GetRecordsPerBatch(NoticeKind.S51));
        }
    }

    /// <summary>Section 49 quality check is per batch.</summary>
    public class NoticeQaServiceTests
    {
        private readonly TestDb _db = new();
        private static readonly Guid Key = Guid.NewGuid();

        private NoticeQaService Service(bool s49QaEnabled = true) =>
            new(_db.NewContext(),
                Mock.Of<IConfiguration>(),
                Mock.Of<INoticeSourceStatusService>(),
                Options.Create(new Section49Options { Qa = new Section49QaOptions { Enabled = s49QaEnabled } }),
                Options.Create(new RollDbOptions()),
                Mock.Of<IRollDbConnectionFactory>());

        /// <summary>Two fully printed S49 batches; QA approved for the first one only.</summary>
        private void SeedTwoPrintedBatchesFirstApproved()
        {
            _db.Seed(db =>
            {
                db.RollRegistry.Add(Make.Supp4Roll());
                db.NoticeSettings.Add(Make.Settings(id: 1, approvalKey: Key));

                db.NoticeBatches.Add(Make.Batch(1, 1, Key, "S49_SUPP 4_0001", 2));
                db.NoticeRunLogs.Add(Make.PrintedLog(11, 1, "P11"));
                db.NoticeRunLogs.Add(Make.PrintedLog(12, 1, "P12"));

                db.NoticeBatches.Add(Make.Batch(2, 1, Key, "S49_SUPP 4_0002", 1));
                db.NoticeRunLogs.Add(Make.PrintedLog(21, 2, "P21"));

                db.NoticeQaRuns.Add(Make.ApprovedQaRun(100, 1, Key, 11));
            });
        }

        [Fact]
        public async Task Second_printed_batch_still_needs_its_own_sample()
        {
            SeedTwoPrintedBatchesFirstApproved();

            Assert.False(await Service().IsQaApprovedAsync(Key, default));
            Assert.Equal(new[] { 1 }, await Service().GetQaApprovedBatchIdsAsync(Key, default));
            Assert.Equal(new[] { "S49_SUPP 4_0002" }, await Service().GetBatchesAwaitingQaAsync(Key, default));
        }

        [Fact]
        public async Task Approving_the_second_batch_clears_the_gate()
        {
            SeedTwoPrintedBatchesFirstApproved();
            _db.Seed(db => db.NoticeQaRuns.Add(Make.ApprovedQaRun(101, 1, Key, 21)));

            Assert.True(await Service().IsQaApprovedAsync(Key, default));
            Assert.Equal(new[] { 1, 2 }, (await Service().GetQaApprovedBatchIdsAsync(Key, default)).OrderBy(x => x));
            Assert.Empty(await Service().GetBatchesAwaitingQaAsync(Key, default));
        }

        [Fact]
        public async Task A_batch_that_is_not_fully_printed_is_not_waiting_for_QA_yet()
        {
            _db.Seed(db =>
            {
                db.RollRegistry.Add(Make.Supp4Roll());
                db.NoticeSettings.Add(Make.Settings(id: 1, approvalKey: Key));
                db.NoticeBatches.Add(Make.Batch(1, 1, Key, "S49_SUPP 4_0001", 2));
                db.NoticeRunLogs.Add(Make.PrintedLog(11, 1, "P11"));
                var pending = Make.PrintedLog(12, 1, "P12");
                pending.Status = RunStatus.Generated;
                db.NoticeRunLogs.Add(pending);
            });

            Assert.Empty(await Service().GetBatchesAwaitingQaAsync(Key, default));
            Assert.False(await Service().IsQaApprovedAsync(Key, default));
        }

        [Fact]
        public async Task S49_QA_can_be_switched_off_in_config()
        {
            SeedTwoPrintedBatchesFirstApproved();

            var svc = Service(s49QaEnabled: false);

            Assert.False(await svc.RequiresQaAsync(Key, default));
            Assert.True(await svc.IsQaApprovedAsync(Key, default));
            Assert.Empty(await svc.GetBatchesAwaitingQaAsync(Key, default));
        }

        [Fact]
        public async Task Other_notices_unlock_every_batch_once_QA_is_approved()
        {
            var key = Guid.NewGuid();
            _db.Seed(db =>
            {
                db.RollRegistry.Add(Make.Supp4Roll());
                db.NoticeSettings.Add(Make.Settings(id: 1, notice: NoticeKind.S51, approvalKey: key));
                db.NoticeBatches.Add(Make.Batch(1, 1, key, "S51_SUPP 4_0001", 1, notice: NoticeKind.S51));
                db.NoticeBatches.Add(Make.Batch(2, 1, key, "S51_SUPP 4_0002", 1, notice: NoticeKind.S51));
            });

            Assert.True(await Service().RequiresQaAsync(key, default));
            Assert.Empty(await Service().GetQaApprovedBatchIdsAsync(key, default));

            _db.Seed(db =>
            {
                var run = Make.ApprovedQaRun(1, 1, key);
                run.Notice = NoticeKind.S51;
                db.NoticeQaRuns.Add(run);
            });

            Assert.Equal(new[] { 1, 2 }, (await Service().GetQaApprovedBatchIdsAsync(key, default)).OrderBy(x => x));
        }
    }

    public class HolidayServiceTests
    {
        [Fact]
        public async Task Finds_national_and_period_specific_holidays()
        {
            var db = new TestDb();
            db.Seed(ctx =>
            {
                ctx.PublicHolidays.Add(new PublicHoliday { Id = 1, HolidayName = "Day of Reconciliation", HolidayDate = new DateOnly(2026, 12, 16), Year = 2026 });
                ctx.PublicHolidays.Add(new PublicHoliday { Id = 2, HolidayName = "GV23 only", HolidayDate = new DateOnly(2026, 12, 17), Year = 2026, ValuationPeriodCode = "GV23" });
            });

            var svc = new HolidayService(db.NewContext(), new MemoryCache(new MemoryCacheOptions()));

            Assert.True(await svc.IsHolidayAsync("GV23", new DateOnly(2026, 12, 16), default));
            Assert.True(await svc.IsHolidayAsync("GV23", new DateOnly(2026, 12, 17), default));
            Assert.False(await svc.IsHolidayAsync("GV18", new DateOnly(2026, 12, 17), default));
            Assert.False(await svc.IsHolidayAsync("GV23", new DateOnly(2026, 12, 18), default));

            var range = await svc.GetHolidaysAsync("GV23", new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 1), default);
            Assert.Equal(2, range.Count); // from/to in the wrong order are swapped
        }
    }

    public class BatchNameServiceTests
    {
        [Fact]
        public async Task Continues_from_the_highest_number_for_the_same_day()
        {
            var db = new TestDb();
            db.Seed(ctx =>
            {
                ctx.NoticeSettings.Add(Make.Settings(id: 1));
                ctx.NoticeBatches.Add(Make.Batch(1, 1, Guid.NewGuid(), "SUPP4_S49_20261009_002", 1));
                ctx.NoticeBatches.Add(Make.Batch(2, 1, Guid.NewGuid(), "SUPP4_S49_20261009_009", 1));
                ctx.NoticeBatches.Add(Make.Batch(3, 1, Guid.NewGuid(), "SUPP4_S49_20261008_050", 1));
            });

            var name = await new BatchNameService(db.NewContext())
                .NextBatchNameAsync(RollCode.SUPP4, NoticeKind.S49, new DateTime(2026, 10, 9), default);

            Assert.Equal("SUPP4_S49_20261009_010", name);
        }
    }
}
