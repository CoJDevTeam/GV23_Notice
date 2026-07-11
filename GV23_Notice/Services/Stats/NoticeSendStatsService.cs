using ClosedXML.Excel;
using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models.Workflow.ViewModels;
using GV23_Notice.Services.QA;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services.Stats
{
    public sealed class NoticeSendStatsService : INoticeSendStatsService
    {
        private readonly AppDbContext _db;
        private readonly INoticeQaService _qa;
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly INoticeStatsEmailService _statsEmail;
        private readonly IThirdPartyAppealStatsService _tpaStats;

        public NoticeSendStatsService(
            AppDbContext db,
            INoticeQaService qa,
            IConfiguration config,
            IWebHostEnvironment env,
            INoticeStatsEmailService statsEmail,
            IThirdPartyAppealStatsService tpaStats)
        {
            _db = db;
            _qa = qa;
            _config = config;
            _env = env;
            _statsEmail = statsEmail;
            _tpaStats = tpaStats;
        }

        public async Task<NoticeSendStatsVm> BuildStatsAsync(
            Guid workflowKey,
            CancellationToken ct)
        {
            if (workflowKey == Guid.Empty)
            {
                throw new ArgumentException(
                    "A valid workflow key is required.",
                    nameof(workflowKey));
            }

            var settings = await _db.NoticeSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        x.WorkflowKey == workflowKey ||
                        x.ApprovalKey == workflowKey,
                    ct)
                ?? throw new InvalidOperationException(
                    "Notice settings not found.");

            if (settings.Notice == NoticeKind.TPA)
            {
                var tpa = await _tpaStats.BuildStatsAsync(
                    workflowKey,
                    ct);

                return new NoticeSendStatsVm
                {
                    WorkflowKey = workflowKey,
                    SettingsId = settings.Id,

                    IsTpa = true,
                    TpaStats = tpa,

                    RollShortCode =
                        tpa.RollShortCode ?? string.Empty,

                    RollName =
                        tpa.RollName ?? string.Empty,

                    Notice = NoticeKind.TPA,

                    VersionText =
                        string.IsNullOrWhiteSpace(tpa.VersionText)
                            ? $"V{settings.Version}"
                            : tpa.VersionText,

                    /*
                     * TPA does not use NoticeBatches.
                     * The value is mapped to the Admin count only
                     * so the shared view model remains complete.
                     */
                    TotalBatches = tpa.TotalAdmins,
                    TotalRecords = tpa.TotalRecords,
                    TotalPrinted = tpa.TotalPrinted,
                    TotalSent = tpa.TotalSent,
                    TotalFailed = tpa.TotalFailed,
                    TotalNoEmail = tpa.TotalNoEmail,

                    SentBy = tpa.LastSentBy,
                    LastSentAtUtc = tpa.LastSentAt,

                    QaRequired = false,
                    QaApproved = false,
                    QaApprovedBy = null,
                    QaApprovedAtUtc = null,

                    DefaultToEmails = null,
                    DefaultCcEmails =
                        tpa.ValuationEnquiriesEmail,

                    Batches = new List<NoticeSendStatsBatchVm>(),
                    Rows = new List<NoticeSendStatsRowVm>()
                };
            }

            return await BuildNormalStatsAsync(
                settings,
                workflowKey,
                ct);
        }

        private async Task<NoticeSendStatsVm> BuildNormalStatsAsync(
            NoticeSettings settings,
            Guid workflowKey,
            CancellationToken ct)
        {
            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.RollId == settings.RollId,
                    ct)
                ?? throw new InvalidOperationException(
                    "Roll not found.");

            var batches = await _db.NoticeBatches
                .AsNoTracking()
                .Where(x => x.WorkflowKey == workflowKey)
                .OrderBy(x => x.BatchName)
                .ToListAsync(ct);

            var batchIds = batches
                .Select(x => x.Id)
                .ToList();

            var logs = await _db.NoticeRunLogs
                .AsNoTracking()
                .Where(x =>
                    batchIds.Contains(x.NoticeBatchId))
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

                IsTpa = false,
                TpaStats = null,

                RollShortCode = roll.ShortCode ?? string.Empty,
                RollName = roll.Name ?? string.Empty,

                Notice = settings.Notice,
                VersionText = $"V{settings.Version}",

                TotalBatches = batches.Count,
                TotalRecords = logs.Count,

                TotalPrinted = logs.Count(
                    x => x.Status == RunStatus.Printed),

                TotalSent = logs.Count(
                    x => x.Status == RunStatus.Sent),

                TotalFailed = logs.Count(
                    x => x.Status == RunStatus.Failed),

                TotalNoEmail = logs.Count(
                    x => x.Status == RunStatus.NoEmail),

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

                QaRequired = await _qa.RequiresQaAsync(
                    workflowKey,
                    ct),

                QaApproved = await _qa.IsQaApprovedAsync(
                    workflowKey,
                    ct),

                QaApprovedBy = qaRun?.ApprovedBy,
                QaApprovedAtUtc = qaRun?.ApprovedAtUtc,

                DefaultToEmails =
                    _config["NoticeStats:DefaultToEmails"],

                DefaultCcEmails =
                    _config["NoticeStats:DefaultCcEmails"]
            };

            vm.Batches = batches
                .Select(batch =>
                {
                    var batchLogs = logs
                        .Where(x =>
                            x.NoticeBatchId == batch.Id)
                        .ToList();

                    return new NoticeSendStatsBatchVm
                    {
                        BatchId = batch.Id,
                        BatchName = batch.BatchName,
                        BatchDate = batch.BatchDate,

                        TotalRecords = batchLogs.Count,

                        Printed = batchLogs.Count(
                            x => x.Status == RunStatus.Printed),

                        Sent = batchLogs.Count(
                            x => x.Status == RunStatus.Sent),

                        Failed = batchLogs.Count(
                            x => x.Status == RunStatus.Failed),

                        NoEmail = batchLogs.Count(
                            x => x.Status == RunStatus.NoEmail)
                    };
                })
                .ToList();

            vm.Rows = logs
                .Select(log =>
                {
                    var batch = batches
                        .FirstOrDefault(
                            x => x.Id == log.NoticeBatchId);

                    return new NoticeSendStatsRowVm
                    {
                        BatchName = batch?.BatchName,

                        ObjectionNo = log.ObjectionNo,
                        AppealNo = log.AppealNo,
                        PremiseId = log.PremiseId,

                        PropertyDesc = log.PropertyDesc,

                        RecipientName = log.RecipientName,
                        RecipientEmail = log.RecipientEmail,

                        Status = log.Status.ToString(),
                        ErrorMessage = log.ErrorMessage,

                        PdfPath = log.PdfPath,
                        EmlPath = log.EmlPath,

                        SentAtUtc = log.SentAtUtc,

                        /*
                         * The current view model uses a lowercase
                         * property name: sentBy.
                         */
                        sentBy = log.SentBy
                    };
                })
                .ToList();

            return vm;
        }

        public async Task<string> GenerateExcelAsync(
            Guid workflowKey,
            string generatedBy,
            CancellationToken ct)
        {
            var vm = await BuildStatsAsync(
                workflowKey,
                ct);

            if (vm.IsTpa)
            {
                throw new InvalidOperationException(
                    "TPA workbooks are generated separately for each Admin by ThirdPartyAppealStatsService.");
            }

            var root =
                _config["NoticeStats:RootPath"];

            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.Combine(
                    _env.ContentRootPath,
                    "App_Data",
                    "NoticeStats");
            }

            var folder = Path.Combine(
                root,
                vm.RollShortCode,
                vm.Notice.ToString(),
                DateTime.Now.ToString("yyyyMMdd"));

            Directory.CreateDirectory(folder);

            var fileName =
                $"{vm.RollShortCode}_{vm.Notice}_{vm.VersionText}_SendStats_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                    .Replace(" ", "_");

            var path =
                Path.Combine(folder, fileName);

            using var workbook =
                new XLWorkbook();

            BuildSummarySheet(
                workbook,
                vm,
                generatedBy);

            BuildBatchSheet(
                workbook,
                vm);

            BuildDetailsSheet(
                workbook,
                vm);

            workbook.SaveAs(path);

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
            {
                throw new InvalidOperationException(
                    "Please enter at least one stakeholder email address.");
            }

            var vm = await BuildStatsAsync(
                workflowKey,
                ct);

            if (vm.IsTpa)
            {
                throw new InvalidOperationException(
                    "TPA statistics must be sent using Send All Admin Reports.");
            }

            var excelPath = await GenerateExcelAsync(
                workflowKey,
                sentBy,
                ct);

            var subject =
                $"Notice Send Stats - {vm.RollShortCode} {vm.Notice} {vm.VersionText}";

            var body = $"""
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
""";

            await _statsEmail.SendStatsEmailAsync(
                toEmails: toEmails,
                ccEmails: ccEmails,
                subject: subject,
                htmlBody: body,
                attachmentPath: excelPath,
                ct: ct);
        }

        private static void BuildSummarySheet(
            XLWorkbook workbook,
            NoticeSendStatsVm vm,
            string generatedBy)
        {
            var sheet =
                workbook.Worksheets.Add("Summary");

            sheet.Cell(1, 1).Value =
                "Notice Send Statistics";

            sheet.Cell(1, 1)
                .Style.Font.Bold = true;

            sheet.Cell(1, 1)
                .Style.Font.FontSize = 16;

            var rowNumber = 3;

            void AddRow(
                string label,
                object? value)
            {
                sheet.Cell(rowNumber, 1).Value =
                    label;

                sheet.Cell(rowNumber, 2).Value =
                    value?.ToString() ?? string.Empty;

                sheet.Cell(rowNumber, 1)
                    .Style.Font.Bold = true;

                rowNumber++;
            }

            AddRow(
                "Roll",
                $"{vm.RollShortCode} - {vm.RollName}");

            AddRow("Notice", vm.Notice);
            AddRow("Version", vm.VersionText);
            AddRow("Total Batches", vm.TotalBatches);
            AddRow("Total Records", vm.TotalRecords);
            AddRow("Printed / Ready", vm.TotalPrinted);
            AddRow("Sent", vm.TotalSent);
            AddRow("Failed", vm.TotalFailed);
            AddRow("No Email", vm.TotalNoEmail);
            AddRow(
                "QA Required",
                vm.QaRequired ? "Yes" : "No");
            AddRow(
                "QA Approved",
                vm.QaApproved ? "Yes" : "No");
            AddRow("QA Approved By", vm.QaApprovedBy);
            AddRow(
                "QA Approved At",
                vm.QaApprovedAtUtc
                    ?.ToLocalTime()
                    .ToString("dd MMM yyyy HH:mm"));
            AddRow("Last Sent By", vm.SentBy);
            AddRow(
                "Last Sent At",
                vm.LastSentAtUtc
                    ?.ToLocalTime()
                    .ToString("dd MMM yyyy HH:mm"));
            AddRow("Generated By", generatedBy);
            AddRow(
                "Generated At",
                DateTime.Now.ToString(
                    "dd MMM yyyy HH:mm"));

            sheet.Columns().AdjustToContents();
        }

        private static void BuildBatchSheet(
            XLWorkbook workbook,
            NoticeSendStatsVm vm)
        {
            var sheet =
                workbook.Worksheets.Add("Batches");

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

            for (var column = 0;
                 column < headers.Length;
                 column++)
            {
                sheet.Cell(1, column + 1).Value =
                    headers[column];

                sheet.Cell(1, column + 1)
                    .Style.Font.Bold = true;
            }

            var rowNumber = 2;

            foreach (var batch in vm.Batches)
            {
                sheet.Cell(rowNumber, 1).Value =
                    batch.BatchName;

                sheet.Cell(rowNumber, 2).Value =
                    batch.BatchDate.ToString(
                        "dd MMM yyyy");

                sheet.Cell(rowNumber, 3).Value =
                    batch.TotalRecords;

                sheet.Cell(rowNumber, 4).Value =
                    batch.Printed;

                sheet.Cell(rowNumber, 5).Value =
                    batch.Sent;

                sheet.Cell(rowNumber, 6).Value =
                    batch.Failed;

                sheet.Cell(rowNumber, 7).Value =
                    batch.NoEmail;

                rowNumber++;
            }

            sheet.Columns().AdjustToContents();
        }

        private static void BuildDetailsSheet(
            XLWorkbook workbook,
            NoticeSendStatsVm vm)
        {
            var sheet =
                workbook.Worksheets.Add(
                    "Notice Details");

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

            for (var column = 0;
                 column < headers.Length;
                 column++)
            {
                sheet.Cell(1, column + 1).Value =
                    headers[column];

                sheet.Cell(1, column + 1)
                    .Style.Font.Bold = true;
            }

            var rowNumber = 2;

            foreach (var row in vm.Rows)
            {
                sheet.Cell(rowNumber, 1).Value =
                    row.BatchName;

                sheet.Cell(rowNumber, 2).Value =
                    row.ObjectionNo;

                sheet.Cell(rowNumber, 3).Value =
                    row.AppealNo;

                sheet.Cell(rowNumber, 4).Value =
                    row.PremiseId;

                sheet.Cell(rowNumber, 5).Value =
                    row.PropertyDesc;

                sheet.Cell(rowNumber, 6).Value =
                    row.RecipientName;

                sheet.Cell(rowNumber, 7).Value =
                    row.RecipientEmail;

                sheet.Cell(rowNumber, 8).Value =
                    row.Status;

                sheet.Cell(rowNumber, 9).Value =
                    row.ErrorMessage;

                sheet.Cell(rowNumber, 10).Value =
                    row.PdfPath;

                sheet.Cell(rowNumber, 11).Value =
                    row.EmlPath;

                sheet.Cell(rowNumber, 12).Value =
                    row.SentAtUtc
                        ?.ToLocalTime()
                        .ToString("dd MMM yyyy HH:mm");

                sheet.Cell(rowNumber, 13).Value =
                    row.sentBy;

                rowNumber++;
            }

            sheet.Columns().AdjustToContents();
        }
    }
}