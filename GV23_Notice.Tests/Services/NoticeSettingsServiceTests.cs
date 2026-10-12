using GV23_Notice.Domain.Workflow;
using GV23_Notice.Services;
using GV23_Notice.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Tests.Services
{
    public class NoticeSettingsServiceTests
    {
        private readonly TestDb _db = new();

        public NoticeSettingsServiceTests()
        {
            _db.Seed(db =>
            {
                db.RollRegistry.Add(Make.Supp4Roll());
                db.RollRegistry.Add(Make.Gv23Roll());
            });
        }

        private NoticeSettingsService Service(out GV23_Notice.Data.AppDbContext ctx)
        {
            ctx = _db.NewContext();
            return new NoticeSettingsService(ctx);
        }

        [Fact]
        public async Task CreateDraft_numbers_versions_per_roll_notice_and_mode()
        {
            var v1 = await Service(out _).CreateDraftAsync(Make.Supp4RollId, NoticeKind.S49, BatchMode.Bulk, "tester", default);
            var v2 = await Service(out _).CreateDraftAsync(Make.Supp4RollId, NoticeKind.S49, BatchMode.Bulk, "tester", default);
            var otherNotice = await Service(out _).CreateDraftAsync(Make.Supp4RollId, NoticeKind.S51, BatchMode.Bulk, "tester", default);

            Assert.Equal(1, v1.Version);
            Assert.Equal(2, v2.Version);
            Assert.Equal(1, otherNotice.Version);
            Assert.Equal(RollCode.SUPP4, v1.Roll);
            Assert.False(v1.IsConfirmed);
            Assert.False(v1.IsApproved);
        }

        [Fact]
        public async Task CreateDraft_writes_a_Created_log()
        {
            var draft = await Service(out _).CreateDraftAsync(Make.Supp4RollId, NoticeKind.S49, BatchMode.Bulk, "tester", default);

            using var check = _db.NewContext();
            var log = await check.NoticeApprovalLogs.SingleAsync(x => x.NoticeSettingsId == draft.Id);
            Assert.Equal(ApprovalAction.Created, log.Action);
            Assert.Equal("tester", log.PerformedBy);
        }

        [Fact]
        public async Task CreateDraft_for_an_unknown_roll_throws()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Service(out _).CreateDraftAsync(999, NoticeKind.S49, BatchMode.Bulk, "tester", default));
        }

        [Fact]
        public async Task CreateDraft_accepts_SUPP4_written_without_a_space()
        {
            _db.Seed(db => db.RollRegistry.Add(new GV23_Notice.Domain.Rolls.RollRegistry
            {
                RollId = 44,
                ShortCode = "SUPP4",
                Name = "Supp 4 (no space)",
                RollType = "Supp",
                SourceDb = "Objection_Supp4b",
                CreatedBy = "test"
            }));

            var draft = await Service(out _).CreateDraftAsync(44, NoticeKind.S49, BatchMode.Bulk, "tester", default);

            Assert.Equal(RollCode.SUPP4, draft.Roll);
        }

        [Fact]
        public async Task CLA_draft_gets_a_30_day_representation_period()
        {
            var draft = await Service(out _).CreateDraftAsync(Make.Supp4RollId, NoticeKind.CLA_TPA, BatchMode.Bulk, "tester", default);

            Assert.Equal(draft.LetterDate.Date, draft.ObjectionStartDate);
            Assert.Equal(draft.LetterDate.Date.AddDays(30), draft.ObjectionEndDate);
            Assert.Null(draft.ExtensionDate);
        }

        [Fact]
        public async Task Approve_before_confirm_throws()
        {
            var draft = await Service(out _).CreateDraftAsync(Make.Supp4RollId, NoticeKind.S49, BatchMode.Bulk, "tester", default);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Service(out _).ApproveAsync(draft.Id, "manager", null, default));
        }

        [Fact]
        public async Task Confirm_then_approve_sets_the_flags_and_a_second_approve_throws()
        {
            var draft = await Service(out _).CreateDraftAsync(Make.Supp4RollId, NoticeKind.S49, BatchMode.Bulk, "tester", default);

            await Service(out _).ConfirmAsync(draft.Id, "tester", null, default);
            await Service(out _).ApproveAsync(draft.Id, "manager", "ok", default);

            var saved = await Service(out _).GetByIdAsync(draft.Id, default);
            Assert.True(saved!.IsConfirmed);
            Assert.True(saved.IsApproved);
            Assert.Equal("manager", saved.ApprovedBy);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Service(out _).ApproveAsync(draft.Id, "manager", null, default));
        }

        [Fact]
        public async Task An_approved_configuration_cannot_be_saved_again()
        {
            var draft = await Service(out _).CreateDraftAsync(Make.Supp4RollId, NoticeKind.S49, BatchMode.Bulk, "tester", default);
            await Service(out _).ConfirmAsync(draft.Id, "tester", null, default);
            await Service(out _).ApproveAsync(draft.Id, "manager", null, default);

            var svc = Service(out _);
            var entity = await svc.GetByIdForUpdateAsync(draft.Id, default);
            entity!.LetterDate = entity.LetterDate.AddDays(1);

            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveDraftAsync(entity, default));
        }

        [Fact]
        public async Task GetLatestDraftOrApproved_returns_the_highest_version()
        {
            await Service(out _).CreateDraftAsync(Make.Supp4RollId, NoticeKind.S49, BatchMode.Bulk, "tester", default);
            await Service(out _).CreateDraftAsync(Make.Supp4RollId, NoticeKind.S49, BatchMode.Bulk, "tester", default);

            var latest = await Service(out _).GetLatestDraftOrApprovedAsync(Make.Supp4RollId, NoticeKind.S49, BatchMode.Bulk, default);

            Assert.Equal(2, latest!.Version);
        }
    }
}
