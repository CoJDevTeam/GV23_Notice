using GV23_Notice.Controllers;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Models.DTOs;
using GV23_Notice.Models.Stats;
using GV23_Notice.Models.Workflow.ViewModels;
using GV23_Notice.Services.Search;
using GV23_Notice.Services.Stats;
using GV23_Notice.Tests.Support;
using Microsoft.AspNetCore.Mvc;

namespace GV23_Notice.Tests.Controllers
{
    public class StatsControllerTests
    {
        private readonly Mock<INoticeStatsDashboardService> _stats = new();
        private readonly Mock<IThirdPartyAppealStatsService> _tpa = new();

        private StatsController Controller() =>
            ControllerSetup.Prepare(new StatsController(_stats.Object, _tpa.Object));

        [Fact]
        public async Task Dashboard_shows_the_service_numbers()
        {
            var vm = new NoticeStatsDashboardVm { TotalSent = 100 };
            _stats.Setup(s => s.BuildDashboardAsync(It.IsAny<NoticeStatsFilterVm>(), It.IsAny<CancellationToken>())).ReturnsAsync(vm);

            var result = await Controller().Index(new NoticeStatsFilterVm { Notice = NoticeKind.S49 }, default);

            var model = Assert.IsType<NoticeStatsDashboardVm>(Assert.IsType<ViewResult>(result).Model);
            Assert.Equal(100, model.TotalSent);
            Assert.False(model.IsTpa);
            _tpa.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Third_party_dashboard_uses_the_third_party_numbers()
        {
            var key = Guid.NewGuid();
            _stats.Setup(s => s.BuildDashboardAsync(It.IsAny<NoticeStatsFilterVm>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new NoticeStatsDashboardVm());
            _tpa.Setup(t => t.BuildStatsAsync(key, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ThirdPartyAppealStatsVm { TotalAdmins = 3, TotalRecords = 40, TotalSent = 35, TotalFailed = 2 });

            var result = await Controller().Index(new NoticeStatsFilterVm { Notice = NoticeKind.TPA, WorkflowKey = key }, default);

            var model = Assert.IsType<NoticeStatsDashboardVm>(Assert.IsType<ViewResult>(result).Model);
            Assert.True(model.IsTpa);
            Assert.Equal(3, model.TotalBatches);
            Assert.Equal(40, model.TotalRecords);
            Assert.Equal(35, model.TotalSent);
        }

        [Fact]
        public async Task Third_party_dashboard_without_a_key_shows_an_error()
        {
            _stats.Setup(s => s.BuildDashboardAsync(It.IsAny<NoticeStatsFilterVm>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new NoticeStatsDashboardVm());

            var controller = Controller();
            await controller.Index(new NoticeStatsFilterVm { Notice = NoticeKind.TPA }, default);

            Assert.NotNull(controller.TempData["Error"]);
            _tpa.VerifyNoOtherCalls();
        }

        [Fact]
        public void Tpa_link_needs_a_key()
        {
            Assert.IsType<BadRequestObjectResult>(Controller().Tpa(Guid.Empty));
            Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(Controller().Tpa(Guid.NewGuid())).ActionName);
        }
    }

    public class NoticeSearchControllerTests
    {
        private readonly Mock<INoticeSearchService> _search = new();

        private NoticeSearchController Controller() =>
            ControllerSetup.Prepare(new NoticeSearchController(_search.Object));

        [Theory]
        [InlineData("ObjectionNo")]
        [InlineData("AppealNo")]
        [InlineData("Property")]
        public async Task Search_uses_the_right_lookup_for_the_mode(string mode)
        {
            var found = new NoticeSearchResult();
            _search.Setup(s => s.SearchByObjectionNoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(found);
            _search.Setup(s => s.SearchByAppealNoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(found);
            _search.Setup(s => s.SearchByPropertyDescAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(found);
            _search.Setup(s => s.GetTownshipsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());

            var result = await Controller().Search(mode, "OBJ-1", "APP-1", "ROSEBANK", null, "12", null, null, default);

            Assert.Same(found, Assert.IsType<ViewResult>(result).Model);
            _search.Verify(s => s.SearchByObjectionNoAsync("OBJ-1", It.IsAny<CancellationToken>()), mode == "ObjectionNo" ? Times.Once() : Times.Never());
            _search.Verify(s => s.SearchByAppealNoAsync("APP-1", It.IsAny<CancellationToken>()), mode == "AppealNo" ? Times.Once() : Times.Never());
        }

        [Theory]
        [InlineData(@"C:\inetpub\enotice\appsettings.json")]
        [InlineData(@"C:\Windows\win.ini")]
        [InlineData("")]
        public async Task ViewPdf_only_serves_pdf_files(string path)
        {
            // Security: this used to serve any file on the server.
            Assert.IsType<BadRequestObjectResult>(await Controller().ViewPdf(path, default));
        }

        [Fact]
        public async Task ViewPdf_missing_pdf_is_404()
        {
            Assert.IsType<NotFoundObjectResult>(await Controller().ViewPdf(@"C:\nope\missing.pdf", default));
        }

        [Fact]
        public async Task ViewPdf_serves_an_existing_pdf()
        {
            var path = Path.Combine(Path.GetTempPath(), $"enotice-{Guid.NewGuid():N}.pdf");
            await File.WriteAllBytesAsync(path, new byte[] { 0x25, 0x50, 0x44, 0x46 });

            try
            {
                var file = Assert.IsType<FileContentResult>(await Controller().ViewPdf(path, default));
                Assert.Equal("application/pdf", file.ContentType);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task Download_with_no_files_is_a_bad_request()
        {
            Assert.IsType<BadRequestObjectResult>(await Controller().Download("", "ObjectionNo", "OBJ-1", default));
            _search.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Download_zips_the_files_and_logs_who_downloaded()
        {
            _search.Setup(s => s.BuildZipAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new byte[] { 1, 2, 3 });

            var result = await Controller().Download("a.pdf\nb.pdf\n", "ObjectionNo", "OBJ-1", default);

            var file = Assert.IsType<FileContentResult>(result);
            Assert.Equal("application/zip", file.ContentType);
            _search.Verify(s => s.LogDownloadAsync(ControllerSetup.UserName, It.IsAny<NoticeSearchResult>(),
                It.IsAny<string>(), 2, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    public class HomeControllerTests
    {
        [Fact]
        public async Task Dashboard_loads_with_an_empty_database()
        {
            var db = new TestDb();
            var controller = ControllerSetup.Prepare(new HomeController(db.NewContext()));

            var result = await controller.Index(default);

            Assert.IsType<HomeIndexVm>(Assert.IsType<ViewResult>(result).Model);
        }

        [Fact]
        public async Task Dashboard_loads_with_workflows_and_batches()
        {
            var db = new TestDb();
            var key = Guid.NewGuid();
            db.Seed(ctx =>
            {
                ctx.RollRegistry.Add(Make.Supp4Roll());
                ctx.NoticeSettings.Add(Make.Settings(id: 1, approvalKey: key));
                ctx.NoticeBatches.Add(Make.Batch(1, 1, key, "S49_SUPP 4_0001", 1));
                ctx.NoticeRunLogs.Add(Make.PrintedLog(1, 1, "P1"));
            });

            var controller = ControllerSetup.Prepare(new HomeController(db.NewContext()));

            Assert.IsType<ViewResult>(await controller.Index(default));
        }

        [Fact]
        public async Task Dashboard_counts_preview_approvals_and_links_with_the_approval_key()
        {
            // Regression: the dashboard read IsStep2Approved, which nothing sets,
            // so "Step 2 approved" was always 0 and the Step 3 link never showed.
            var db = new TestDb();
            var key = Guid.NewGuid();
            db.Seed(ctx =>
            {
                ctx.RollRegistry.Add(Make.Supp4Roll());
                var s = Make.Settings(id: 1, approvalKey: key);
                s.Step2Approved = true;
                ctx.NoticeSettings.Add(s);
                ctx.NoticeSettings.Add(Make.Settings(id: 2, version: 2)); // approved dates, preview not yet approved
            });

            var controller = ControllerSetup.Prepare(new HomeController(db.NewContext()));
            var vm = Assert.IsType<HomeIndexVm>(Assert.IsType<ViewResult>(await controller.Index(default)).Model);

            Assert.Equal(1, vm.Step2Approved);
            Assert.Equal(1, vm.PendingStep2);

            var row = vm.RecentWorkflows.Single(r => r.Id == 1);
            Assert.True(row.IsStep2Approved);
            Assert.True(row.HasWorkflowKey);
            Assert.Equal(key, row.WorkflowKey);
        }
    }
}
