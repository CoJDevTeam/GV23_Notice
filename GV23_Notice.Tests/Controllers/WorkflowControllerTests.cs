using GV23_Notice.Controllers;
using GV23_Notice.Domain.Email;
using GV23_Notice.Domain.Rolls;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models.DTOs;
using GV23_Notice.Models.Workflow.ViewModels;
using GV23_Notice.Services;
using GV23_Notice.Services.Audit;
using GV23_Notice.Services.ClaThirdPartyApplications;
using GV23_Notice.Services.Email;
using GV23_Notice.Services.Preview;
using GV23_Notice.Services.Preview.GV23_Notice.Services.Notices;
using GV23_Notice.Services.Rolls;
using GV23_Notice.Services.SnapShotStep2;
using GV23_Notice.Services.Step3;
using GV23_Notice.Services.ThirdPartyApplications;
using GV23_Notice.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GV23_Notice.Tests.Controllers
{
    /// <summary>
    /// WorkflowController: Date Configuration → Summary → Notice Preview → Step 3 kickoff.
    /// Everything except the database is a Moq mock.
    /// </summary>
    public class WorkflowControllerTests
    {
        private readonly TestDb _db = new();

        private readonly Mock<INoticeSettingsService> _settings = new();
        private readonly Mock<INoticePreviewService> _preview = new();
        private readonly Mock<IWorkflowApprovalEmailService> _wfEmails = new();
        private readonly Mock<INoticeEmailArchiveService> _archive = new();
        private readonly Mock<INoticeStep2SnapshotService> _snap = new();
        private readonly Mock<IStep3BatchService> _batches = new();
        private readonly Mock<IThirdPartyAppealDateConfigurationService> _tpaDates = new();

        private static readonly Guid Key = Guid.NewGuid();

        public WorkflowControllerTests()
        {
            _db.Seed(db => db.RollRegistry.Add(Make.Supp4Roll()));

            _batches.Setup(b => b.PeekNextBatchNameAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync("S49_SUPP 4_0026");
            _batches.Setup(b => b.GetRecordsPerBatch(It.IsAny<NoticeKind>())).Returns(10);
        }

        private WorkflowController Controller(Action<Microsoft.AspNetCore.Http.HttpContext>? http = null) =>
            ControllerSetup.Prepare(
                new WorkflowController(
                    _db.NewContext(),
                    _settings.Object,
                    Mock.Of<IWorkflowAssetStorage>(),
                    Mock.Of<IS53AppealCloseDateCalculator>(),
                    NullLogger<WorkflowController>.Instance,
                    _preview.Object,
                    _wfEmails.Object,
                    _archive.Object,
                    Options.Create(new EmailTemplateOptions()),
                    Mock.Of<IS49RollRepository>(),
                    Mock.Of<INoticeSettingsAuditService>(),
                    Mock.Of<IPreviewDbDataService>(),
                    Mock.Of<ITempFileStore>(),
                    _snap.Object,
                    Mock.Of<IS52RangePrintService>(),
                    _tpaDates.Object,
                    Mock.Of<IThirdPartyAppealWorkflowSyncService>(),
                    new ClaThirdPartyDateConfigurationService(),
                    _batches.Object),
                http);

        private void GivenSettings(NoticeSettings s)
        {
            _db.Seed(db => db.NoticeSettings.Add(s));
            _settings.Setup(x => x.GetByIdAsync(s.Id, It.IsAny<CancellationToken>())).ReturnsAsync(s);
        }

        // ── Date summary (modal) ─────────────────────────────────────

        [Fact]
        public async Task Step1Summary_returns_the_modal_partial_when_asked()
        {
            GivenSettings(Make.Settings(id: 7, approved: false));

            var result = await Controller().Step1Summary(7, partial: true, default);

            var partial = Assert.IsType<PartialViewResult>(result);
            Assert.Equal("_DateSummaryModal", partial.ViewName);
            var vm = Assert.IsType<WorkflowStep1SummaryVm>(partial.Model);
            Assert.Equal("SUPP 4", vm.RollShortCode);
        }

        [Fact]
        public async Task Step1Summary_returns_the_partial_for_ajax_requests()
        {
            GivenSettings(Make.Settings(id: 7, approved: false));

            var result = await Controller(http => http.Request.Headers["X-Requested-With"] = "XMLHttpRequest")
                .Step1Summary(7, partial: false, default);

            Assert.IsType<PartialViewResult>(result);
        }

        [Fact]
        public async Task Step1Summary_returns_the_full_page_otherwise()
        {
            GivenSettings(Make.Settings(id: 7, approved: false));

            Assert.IsType<ViewResult>(await Controller().Step1Summary(7, partial: false, default));
        }

        [Fact]
        public async Task Step1Summary_unknown_settings_is_404()
        {
            Assert.IsType<NotFoundResult>(await Controller().Step1Summary(404, partial: true, default));
        }

        // ── Approve dates ────────────────────────────────────────────

        [Fact]
        public async Task Approving_dates_twice_goes_to_the_preview_without_approving_again()
        {
            GivenSettings(Make.Settings(id: 7, approved: true));

            var result = await Controller().Step1SummaryApprove(7, default);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Step2", redirect.ActionName);
            _settings.Verify(x => x.ApproveAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Approving_confirmed_S49_dates_approves_and_opens_the_preview()
        {
            GivenSettings(Make.Settings(id: 7, approved: false, confirmed: true));

            var result = await Controller().Step1SummaryApprove(7, default);

            Assert.Equal("Step2", Assert.IsType<RedirectToActionResult>(result).ActionName);
            _settings.Verify(x => x.ApproveAsync(7, ControllerSetup.UserName, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task S49_without_a_signature_cannot_be_approved()
        {
            var s = Make.Settings(id: 7, approved: false, confirmed: true);
            s.SignaturePath = null;
            GivenSettings(s);

            var controller = Controller();
            var result = await controller.Step1SummaryApprove(7, default);

            Assert.Equal("Step1", Assert.IsType<RedirectToActionResult>(result).ActionName);
            Assert.NotNull(controller.TempData["Error"]);
            _settings.Verify(x => x.ApproveAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Unconfirmed_dates_cannot_be_approved()
        {
            GivenSettings(Make.Settings(id: 7, approved: false, confirmed: false));

            var result = await Controller().Step1SummaryApprove(7, default);

            Assert.Equal("Step1Summary", Assert.IsType<RedirectToActionResult>(result).ActionName);
            _settings.Verify(x => x.ApproveAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // ── Approve the notice preview ───────────────────────────────

        [Fact]
        public async Task Approving_an_already_approved_preview_does_nothing_but_continue()
        {
            // Regression: every click used to rebuild the snapshot and send
            // "[APPROVED] … Step 3 Kickoff Ready" again.
            var s = Make.Settings(id: 7, approvalKey: Key);
            s.Step2Approved = true;
            GivenSettings(s);

            var result = await Controller().Step2Approve(
                new Step2ApproveDto { SettingsId = 7, Variant = PreviewVariant.Default, Mode = PreviewMode.Single }, default);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Step3Kickoff", redirect.ActionName);
            Assert.Equal(Key, redirect.RouteValues!["key"]);

            _preview.VerifyNoOtherCalls();
            _snap.VerifyNoOtherCalls();
            _wfEmails.VerifyNoOtherCalls();
            _archive.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task First_preview_approval_saves_a_snapshot_and_opens_the_batch_page()
        {
            GivenSettings(Make.Settings(id: 7, approvalKey: null));

            _preview.Setup(p => p.BuildPreviewAsync(7, PreviewVariant.Default, PreviewMode.Single, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new NoticePreviewResult { PdfBytes = new byte[] { 1 }, PdfFileName = "p.pdf", EmailSubject = "s", EmailBodyHtml = "b" });
            _snap.Setup(x => x.SaveApprovalAsync(It.IsAny<int>(), It.IsAny<PreviewVariant>(), It.IsAny<PreviewMode>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(1);
            _wfEmails.Setup(x => x.BuildApprovalEmail(It.IsAny<NoticeSettings>(), It.IsAny<RollRegistry>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
                     .Returns(("Subject", "<p>Body</p>"));
            _archive.Setup(x => x.SaveAsync(It.IsAny<int>(), It.IsAny<DataDomain>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(@"C:\archive\approval.eml");

            var result = await Controller().Step2Approve(
                new Step2ApproveDto { SettingsId = 7, Variant = PreviewVariant.Default, Mode = PreviewMode.Single }, default);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Step3Kickoff", redirect.ActionName);
            Assert.Equal(true, redirect.RouteValues!["fromStep2"]);

            _snap.Verify(x => x.SaveApprovalAsync(7, PreviewVariant.Default, PreviewMode.Single, ControllerSetup.UserName,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

            using var check = _db.NewContext();
            var saved = await check.NoticeSettings.SingleAsync(x => x.Id == 7);
            Assert.NotNull(saved.ApprovalKey);
            Assert.Equal(@"C:\archive\approval.eml", saved.ApprovedEmailSavedPath);
        }

        // ── Step 3 kickoff (batch page) ──────────────────────────────

        [Fact]
        public async Task Kickoff_with_a_wrong_key_is_refused()
        {
            GivenSettings(Make.Settings(id: 7, approvalKey: Key));

            var result = await Controller().Step3Kickoff(7, Guid.NewGuid(), null, null, null, fromStep2: false, default);

            Assert.IsType<UnauthorizedObjectResult>(result);
        }

        [Fact]
        public async Task Kickoff_for_unapproved_dates_is_refused()
        {
            GivenSettings(Make.Settings(id: 7, approved: false, approvalKey: Key));

            Assert.IsType<BadRequestObjectResult>(
                await Controller().Step3Kickoff(7, Key, null, null, null, fromStep2: false, default));
        }

        [Fact]
        public async Task Kickoff_shows_the_next_batch_number_from_the_batch_service()
        {
            GivenSettings(Make.Settings(id: 7, approvalKey: Key));
            _db.Seed(db => db.NoticeBatches.Add(Make.Batch(1, 7, Key, "S49_SUPP 4_0025", 10)));

            var result = await Controller().Step3Kickoff(7, Key, null, null, null, fromStep2: false, default);

            var vm = Assert.IsType<WorkflowStep3KickoffVm>(Assert.IsType<ViewResult>(result).Model);
            Assert.Equal("S49_SUPP 4_0026", vm.NextBatchCode);
            Assert.Equal(10, vm.RecordsPerBatch);
            Assert.Equal(1, vm.BatchesAlreadyCreated);
            Assert.Single(vm.CreatedBatches);
            Assert.False(vm.FromStep2);
        }

        [Fact]
        public async Task Kickoff_from_preview_approval_offers_the_first_batch()
        {
            GivenSettings(Make.Settings(id: 7, approvalKey: Key));

            var result = await Controller().Step3Kickoff(7, Key, null, null, null, fromStep2: true, default);

            var vm = Assert.IsType<WorkflowStep3KickoffVm>(Assert.IsType<ViewResult>(result).Model);
            Assert.True(vm.FromStep2);
            Assert.Empty(vm.CreatedBatches);
        }

        [Fact]
        public async Task Kickoff_does_not_build_a_preview_pdf()
        {
            GivenSettings(Make.Settings(id: 7, approvalKey: Key));

            await Controller().Step3Kickoff(7, Key, null, null, null, fromStep2: false, default);

            _preview.VerifyNoOtherCalls();
        }
    }
}
