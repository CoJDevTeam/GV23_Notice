using ClosedXML.Excel;
using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Models.Workflow.ViewModels;
using GV23_Notice.Services.Email;
using GV23_Notice.Services.QA;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;

namespace GV23_Notice.Services.Stats
{
    public sealed class NoticeSendStatsService : INoticeSendStatsService
    {
        private readonly AppDbContext _db;
        private readonly INoticeQaService _qa;
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly INoticeStatsEmailService _statsEmail;

        public NoticeSendStatsService(
            AppDbContext db,
            INoticeQaService qa,
            IConfiguration config,
            IWebHostEnvironment env,
            INoticeStatsEmailService statsEmail)
        {
            _db = db;
            _qa = qa;
            _config = config;
            _env = env;
            _statsEmail = statsEmail;
           
        }

        public async Task<NoticeSendStatsVm> BuildStatsAsync(Guid workflowKey, CancellationToken ct)
        {
            var settings = await _db.NoticeSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.WorkflowKey == workflowKey || x.ApprovalKey == workflowKey, ct)
                ?? throw new InvalidOperationException("Notice settings not found.");

            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.RollId == settings.RollId, ct)
                ?? throw new InvalidOperationException("Roll not found.");

            var batches = await _db.NoticeBatches
                .AsNoTracking()
                .Where(x => x.WorkflowKey == workflowKey)
                .OrderBy(x => x.BatchName)
                .ToListAsync(ct);

            var batchIds = batches.Select(x => x.Id).ToList();

            var logs = await _db.NoticeRunLogs
                .AsNoTracking()
                .Where(x => batchIds.Contains(x.NoticeBatchId))
                .OrderBy(x => x.Id)
                .ToListAsync(ct);

            var qaRun = await _db.NoticeQaRuns
                .AsNoTracking()
                .Where(x => x.WorkflowKey == workflowKey)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            var vm = new NoticeSendStatsVm
            {
                WorkflowKey = workflowKey,
                SettingsId = settings.Id,

                RollShortCode = roll.ShortCode ?? "",
                RollName = roll.Name ?? "",

                Notice = settings.Notice,
                VersionText = $"V{settings.Version}",

                TotalBatches = batches.Count,
                TotalRecords = logs.Count,

                TotalPrinted = logs.Count(x => x.Status == RunStatus.Printed),
                TotalSent = logs.Count(x => x.Status == RunStatus.Sent),
                TotalFailed = logs.Count(x => x.Status == RunStatus.Failed),
                TotalNoEmail = logs.Count(x => x.Status == RunStatus.NoEmail),

                SentBy = logs
                    .Where(x => x.Status == RunStatus.Sent)
                    .OrderByDescending(x => x.SentAtUtc)
                    .Select(x => x.SentBy)
                    .FirstOrDefault(),

                LastSentAtUtc = logs
                    .Where(x => x.Status == RunStatus.Sent)
                    .OrderByDescending(x => x.SentAtUtc)
                    .Select(x => x.SentAtUtc)
                    .FirstOrDefault(),

                QaRequired = await _qa.RequiresQaAsync(workflowKey, ct),
                QaApproved = await _qa.IsQaApprovedAsync(workflowKey, ct),
                QaApprovedBy = qaRun?.ApprovedBy,
                QaApprovedAtUtc = qaRun?.ApprovedAtUtc,
                DefaultToEmails = _config["NoticeStats:DefaultToEmails"],
                DefaultCcEmails = _config["NoticeStats:DefaultCcEmails"],
            };

            vm.Batches = batches.Select(b =>
            {
                var batchLogs = logs.Where(x => x.NoticeBatchId == b.Id).ToList();

                return new NoticeSendStatsBatchVm
                {
                    BatchId = b.Id,
                    BatchName = b.BatchName,
                    BatchDate = b.BatchDate,

                    TotalRecords = batchLogs.Count,
                    Printed = batchLogs.Count(x => x.Status == RunStatus.Printed),
                    Sent = batchLogs.Count(x => x.Status == RunStatus.Sent),
                    Failed = batchLogs.Count(x => x.Status == RunStatus.Failed),
                    NoEmail = batchLogs.Count(x => x.Status == RunStatus.NoEmail)
                };
            }).ToList();

            vm.Rows = logs.Select(x =>
            {
                var batch = batches.FirstOrDefault(b => b.Id == x.NoticeBatchId);

                return new NoticeSendStatsRowVm
                {
                    BatchName = batch?.BatchName,

                    ObjectionNo = x.ObjectionNo,
                    AppealNo = x.AppealNo,
                    PremiseId = x.PremiseId,

                    PropertyDesc = x.PropertyDesc,

                    RecipientName = x.RecipientName,
                    RecipientEmail = x.RecipientEmail,

                    Status = x.Status.ToString(),
                    ErrorMessage = x.ErrorMessage,

                    PdfPath = x.PdfPath,
                    EmlPath = x.EmlPath,

                    SentAtUtc = x.SentAtUtc
                };
            }).ToList();

            return vm;
        }

        public async Task<string> GenerateExcelAsync(Guid workflowKey, string generatedBy, CancellationToken ct)
        {
            var vm = await BuildStatsAsync(workflowKey, ct);

            var root = _config["NoticeStats:RootPath"];

            if (string.IsNullOrWhiteSpace(root))
                root = Path.Combine(_env.ContentRootPath, "App_Data", "NoticeStats");

            var folder = Path.Combine(
                root,
                vm.RollShortCode,
                vm.Notice.ToString(),
                DateTime.Now.ToString("yyyyMMdd"));

            Directory.CreateDirectory(folder);

            var fileName =
                $"{vm.RollShortCode}_{vm.Notice}_{vm.VersionText}_SendStats_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                    .Replace(" ", "_");

            var path = Path.Combine(folder, fileName);

            using var wb = new XLWorkbook();

            BuildSummarySheet(wb, vm, generatedBy);
            BuildBatchSheet(wb, vm);
            BuildDetailsSheet(wb, vm);

            wb.SaveAs(path);

            return path;
        }

        public async Task SendStatsEmailAsync(
        Guid workflowKey,
        string toEmails,
        string? ccEmails,
        string sentBy,
        CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(toEmails))
                throw new InvalidOperationException("Please enter at least one stakeholder email address.");

            var vm = await BuildStatsAsync(workflowKey, ct);
            var excelPath = await GenerateExcelAsync(workflowKey, sentBy, ct);

            var subject = $"Notice Send Stats - {vm.RollShortCode} {vm.Notice} {vm.VersionText}";

            var body = $@"
Good day,<br/><br/>

Please find attached the notice sending statistics report.<br/><br/>

<b>Roll:</b> {vm.RollShortCode} - {vm.RollName}<br/>
<b>Notice:</b> {vm.Notice}<br/>
<b>Version:</b> {vm.VersionText}<br/>
<b>Total Records:</b> {vm.TotalRecords}<br/>
<b>Sent:</b> {vm.TotalSent}<br/>
<b>Failed:</b> {vm.TotalFailed}<br/>
<b>No Email:</b> {vm.TotalNoEmail}<br/>
<b>Generated By:</b> {sentBy}<br/>
<b>Generated At:</b> {DateTime.Now:dd MMM yyyy HH:mm}<br/><br/>

Regards,<br/>
Valuation Notice System
";

            await _statsEmail.SendStatsEmailAsync(
                toEmails: toEmails,
                ccEmails: ccEmails,
                subject: subject,
                htmlBody: body,
                attachmentPath: excelPath,
                ct: ct);
        }

        private static void BuildSummarySheet(XLWorkbook wb, NoticeSendStatsVm vm, string generatedBy)
        {
            var ws = wb.Worksheets.Add("Summary");

            ws.Cell(1, 1).Value = "Notice Send Statistics";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 16;

            var r = 3;

            void Row(string label, object? value)
            {
                ws.Cell(r, 1).Value = label;
                ws.Cell(r, 2).Value = value?.ToString() ?? "";
                ws.Cell(r, 1).Style.Font.Bold = true;
                r++;
            }

            Row("Roll", $"{vm.RollShortCode} - {vm.RollName}");
            Row("Notice", vm.Notice);
            Row("Version", vm.VersionText);
            Row("Total Batches", vm.TotalBatches);
            Row("Total Records", vm.TotalRecords);
            Row("Printed / Ready", vm.TotalPrinted);
            Row("Sent", vm.TotalSent);
            Row("Failed", vm.TotalFailed);
            Row("No Email", vm.TotalNoEmail);
            Row("QA Required", vm.QaRequired ? "Yes" : "No");
            Row("QA Approved", vm.QaApproved ? "Yes" : "No");
            Row("QA Approved By", vm.QaApprovedBy);
            Row("QA Approved At", vm.QaApprovedAtUtc?.ToLocalTime().ToString("dd MMM yyyy HH:mm"));
            Row("Last Sent By", vm.SentBy);
            Row("Last Sent At", vm.LastSentAtUtc?.ToLocalTime().ToString("dd MMM yyyy HH:mm"));
            Row("Generated By", generatedBy);
            Row("Generated At", DateTime.Now.ToString("dd MMM yyyy HH:mm"));

            ws.Columns().AdjustToContents();
        }

        private static void BuildBatchSheet(XLWorkbook wb, NoticeSendStatsVm vm)
        {
            var ws = wb.Worksheets.Add("Batches");

            var headers = new[]
            {
                "Batch Name",
                "Batch Date",
                "Total Records",
                "Printed",
                "Sent",
                "Failed",
                "No Email"
            };

            for (var i = 0; i < headers.Length; i++)
            {
                ws.Cell(1, i + 1).Value = headers[i];
                ws.Cell(1, i + 1).Style.Font.Bold = true;
            }

            var r = 2;

            foreach (var b in vm.Batches)
            {
                ws.Cell(r, 1).Value = b.BatchName;
                ws.Cell(r, 2).Value = b.BatchDate.ToString("dd MMM yyyy");
                ws.Cell(r, 3).Value = b.TotalRecords;
                ws.Cell(r, 4).Value = b.Printed;
                ws.Cell(r, 5).Value = b.Sent;
                ws.Cell(r, 6).Value = b.Failed;
                ws.Cell(r, 7).Value = b.NoEmail;
                r++;
            }

            ws.Columns().AdjustToContents();
        }

        private static void BuildDetailsSheet(XLWorkbook wb, NoticeSendStatsVm vm)
        {
            var ws = wb.Worksheets.Add("Notice Details");

            var headers = new[]
            {
                "Batch Name",
                "Objection No",
                "Appeal No",
                "Premise ID",
                "Property Description",
                "Recipient Name",
                "Recipient Email",
                "Status",
                "Error Message",
                "PDF Path",
                "Email Copy Path",
                "Sent At",
                "Sent By"
            };

            for (var i = 0; i < headers.Length; i++)
            {
                ws.Cell(1, i + 1).Value = headers[i];
                ws.Cell(1, i + 1).Style.Font.Bold = true;
            }

            var r = 2;

            foreach (var x in vm.Rows)
            {
                ws.Cell(r, 1).Value = x.BatchName;
                ws.Cell(r, 2).Value = x.ObjectionNo;
                ws.Cell(r, 3).Value = x.AppealNo;
                ws.Cell(r, 4).Value = x.PremiseId;
                ws.Cell(r, 5).Value = x.PropertyDesc;
                ws.Cell(r, 6).Value = x.RecipientName;
                ws.Cell(r, 7).Value = x.RecipientEmail;
                ws.Cell(r, 8).Value = x.Status;
                ws.Cell(r, 9).Value = x.ErrorMessage;
                ws.Cell(r, 10).Value = x.PdfPath;
                ws.Cell(r, 11).Value = x.EmlPath;
                ws.Cell(r, 12).Value = x.SentAtUtc?.ToLocalTime().ToString("dd MMM yyyy HH:mm");
                ws.Cell(r, 13).Value = x.sentBy;
                r++;
            }

            ws.Columns().AdjustToContents();
        }
      
    }
}