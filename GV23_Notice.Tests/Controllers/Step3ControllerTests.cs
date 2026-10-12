using GV23_Notice.Controllers;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Models.Workflow.ViewModels;
using GV23_Notice.Services.ClaThirdPartyApplications;
using GV23_Notice.Services.Email;
using GV23_Notice.Services.QA;
using GV23_Notice.Services.Stats;
using GV23_Notice.Services.Step3;
using GV23_Notice.Services.Storage;
using GV23_Notice.Services.ThirdPartyApplications;
using GV23_Notice.Tests.Support;
using Microsoft.AspNetCore.Mvc;

namespace GV23_Notice.Tests.Controllers
{
    /// <summary>Step3Controller: batches, printing, quality check and sending.</summary>
    public class Step3ControllerTests
    {
        private readonly TestDb _db = new();
        private static readonly Guid Key = Guid.NewGuid();

        private readonly Mock<IStep3WorkflowSelectService> _select = new();
        private readonly Mock<IStep3BatchService> _batchCreate = new();
        private readonly Mock<INoticeBatchPrintService> _print = new();
        private readonly Mock<INoticeBatchEmailService> _email = new();
        private readonly Mock<INoticeQaService> _qa = new();

        private Step3Controller Controller() =>
            ControllerSetup.Prepare(new Step3Controller(
                Mock.Of<IStep3Step1Service>(),
                _select.Object,
                Mock.Of<IStep3BatchQueryService>(),
                _batchCreate.Object,
                _print.Object,
                Mock.Of<IStep3PrintQueryService>(),
                _email.Object,
                Mock.Of<IS52RangePrintService>(),
                _db.NewContext(),
                _qa.Object,
                Mock.Of<INoticeSendStatsService>(),
                Mock.Of<IThirdPartyAppealStatsService>(),
                Mock.Of<IThirdPartyAppealPrintService>(),
                Mock.Of<IThirdPartyAppealEmailService>(),
                Mock.Of<IClaThirdPartyApplicationPrintService>(),
                Mock.Of<IClaThirdPartyApplicationEmailService>()));

        private void GivenWorkflow(NoticeKind notice = NoticeKind.S49) =>
            _db.Seed(db =>
            {
                db.RollRegistry.Add(Make.Supp4Roll());
                db.NoticeSettings.Add(Make.Settings(id: 1, notice: notice, approvalKey: Key));
            });

        // ── List / redirects ─────────────────────────────────────────

        [Fact]
        public async Task Index_shows_the_approved_notices()
        {
            var vm = new Step3SelectVm();
            _select.Setup(s => s.BuildAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync(vm);

            var result = await Controller().Index(null, null, default);

            Assert.Same(vm, Assert.IsType<ViewResult>(result).Model);
        }

        [Fact]
        public void Empty_workflow_key_is_a_bad_request()
        {
            Assert.IsType<BadRequestObjectResult>(Controller().Open(Guid.Empty));
            Assert.IsType<BadRequestObjectResult>(Controller().Kickoff(Guid.Empty));
        }

        // ── Create batch ─────────────────────────────────────────────

        [Fact]
        public async Task Create_batch_goes_to_printing()
        {
            GivenWorkflow();
            _batchCreate.Setup(b => b.CreateBatchAsync(Key, It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(42);

            var controller = Controller();
            var result = await controller.Step2CreateBatch(Key, new DateTime(2026, 10, 9), default);

            Assert.Equal("Print", Assert.IsType<RedirectToActionResult>(result).ActionName);
            Assert.NotNull(controller.TempData["Success"]);
            _batchCreate.Verify(b => b.CreateBatchAsync(Key, new DateTime(2026, 10, 9), ControllerSetup.UserName, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Create_batch_failure_stays_on_the_batch_page_with_the_message()
        {
            GivenWorkflow();
            _batchCreate.Setup(b => b.CreateBatchAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new InvalidOperationException("No Section 49 properties remain available for batching."));

            var controller = Controller();
            var result = await controller.Step2CreateBatch(Key, null, default);

            Assert.Equal("Step2", Assert.IsType<RedirectToActionResult>(result).ActionName);
            Assert.Equal("No Section 49 properties remain available for batching.", controller.TempData["Error"]);
        }

        [Theory]
        [InlineData(NoticeKind.TPA)]
        [InlineData(NoticeKind.CLA_TPA)]
        public async Task Direct_print_notices_never_create_batches(NoticeKind notice)
        {
            GivenWorkflow(notice);

            var controller = Controller();
            var result = await controller.Step2CreateBatch(Key, null, default);

            Assert.Equal("Print", Assert.IsType<RedirectToActionResult>(result).ActionName);
            Assert.NotNull(controller.TempData["Error"]);
            _batchCreate.VerifyNoOtherCalls();
        }

        // ── Print ────────────────────────────────────────────────────

        [Fact]
        public async Task Printing_a_batch_goes_to_quality_check_when_required()
        {
            GivenWorkflow();
            _print.Setup(p => p.PrintBatchAsync(5, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new PrintBatchResult { BatchId = 5, Total = 10, Printed = 10 });
            _qa.Setup(q => q.RequiresQaAsync(Key, It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var result = await Controller().PrintBatch(5, Key, default);

            Assert.Equal("QA", Assert.IsType<RedirectToActionResult>(result).ActionName);
        }

        [Fact]
        public async Task Printing_a_batch_stays_on_print_when_no_quality_check_is_needed()
        {
            GivenWorkflow();
            _print.Setup(p => p.PrintBatchAsync(5, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new PrintBatchResult { BatchId = 5, Total = 10, Printed = 9, Failed = 1 });
            _qa.Setup(q => q.RequiresQaAsync(Key, It.IsAny<CancellationToken>())).ReturnsAsync(false);

            var controller = Controller();
            var result = await controller.PrintBatch(5, Key, default);

            Assert.Equal("Print", Assert.IsType<RedirectToActionResult>(result).ActionName);
            Assert.Contains("1 failed", controller.TempData["Success"]?.ToString());
        }

        // ── Quality check ────────────────────────────────────────────

        [Fact]
        public async Task Quality_check_page_skips_to_sending_when_not_required()
        {
            _qa.Setup(q => q.RequiresQaAsync(Key, It.IsAny<CancellationToken>())).ReturnsAsync(false);

            var result = await Controller().QA(Key, default);

            Assert.Equal("SendEmail", Assert.IsType<RedirectToActionResult>(result).ActionName);
        }

        [Fact]
        public async Task Approving_quality_check_goes_to_sending()
        {
            var controller = Controller();
            var result = await controller.ApproveQA(Key, 9, "looks good", default);

            Assert.Equal("SendEmail", Assert.IsType<RedirectToActionResult>(result).ActionName);
            _qa.Verify(q => q.ApproveQaAsync(Key, 9, ControllerSetup.UserName, "looks good", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Failed_quality_check_approval_stays_on_the_quality_check_page()
        {
            _qa.Setup(q => q.ApproveQaAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("QA cannot be approved."));

            var controller = Controller();
            var result = await controller.ApproveQA(Key, 9, null, default);

            Assert.Equal("QA", Assert.IsType<RedirectToActionResult>(result).ActionName);
            Assert.Equal("QA cannot be approved.", controller.TempData["Error"]);
        }

        [Fact]
        public async Task Create_quality_check_sample_error_is_shown()
        {
            _qa.Setup(q => q.RequiresQaAsync(Key, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _qa.Setup(q => q.CreateQaRunAsync(Key, It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("No completely printed Section 49 batch is waiting for QA."));

            var controller = Controller();
            var result = await controller.CreateQA(Key, default);

            Assert.Equal("QA", Assert.IsType<RedirectToActionResult>(result).ActionName);
            Assert.NotNull(controller.TempData["Error"]);
        }

        // ── Send ─────────────────────────────────────────────────────

        [Fact]
        public async Task Sending_without_choosing_a_batch_is_refused()
        {
            GivenWorkflow();

            var controller = Controller();
            var result = await controller.SendEmail(Key, new List<int>(), default);

            Assert.Equal("SendEmail", Assert.IsType<RedirectToActionResult>(result).ActionName);
            Assert.NotNull(controller.TempData["Error"]);
            _email.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Sending_selected_batches_reports_the_result_and_opens_the_statistics()
        {
            GivenWorkflow();
            _email.Setup(e => e.SendBatchEmailsAsync(It.IsAny<IEnumerable<int>>(), Key, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SendBatchEmailResult { Sent = 3, Failed = 1, Skipped = 0 });

            var controller = Controller();
            var result = await controller.SendEmail(Key, new List<int> { 1, 2 }, default);

            Assert.Equal("SendStats", Assert.IsType<RedirectToActionResult>(result).ActionName);
            Assert.Contains("Emails sent: 3", controller.TempData["Success"]?.ToString());
            _email.Verify(e => e.SendBatchEmailsAsync(
                It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 1, 2 })),
                Key, ControllerSetup.UserName, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Send_gate_message_is_shown_as_an_error()
        {
            GivenWorkflow();
            _email.Setup(e => e.SendBatchEmailsAsync(It.IsAny<IEnumerable<int>>(), Key, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SendBatchEmailResult { ErrorMessage = "Section 49 QA must be approved for every selected batch before notices can be sent." });

            var controller = Controller();
            await controller.SendEmail(Key, new List<int> { 2 }, default);

            Assert.Equal("Section 49 QA must be approved for every selected batch before notices can be sent.", controller.TempData["Error"]);
            Assert.Null(controller.TempData["Success"]);
        }
    }
}
