using GV23_Notice.Data;
using GV23_Notice.Domain.Email;
using GV23_Notice.Domain.Rolls;
using GV23_Notice.Domain.Section49;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Services.QA;
using GV23_Notice.Services.Rolls;
using GV23_Notice.Services.Storage;
using GV23_Notice.Services.Workflow;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Data;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace GV23_Notice.Services.Email
{
    public sealed class NoticeBatchEmailService : INoticeBatchEmailService
    {
        private const int HardMaxEmails = 2000;

        private readonly AppDbContext _db;
        private readonly EmailOptions _emailOpt;
        private readonly Section49Options _section49;
        private readonly RollDbOptions _rollDb;

        private readonly INoticeEmailTemplateService _templates;
        private readonly INoticePathService _paths;
        private readonly IS49RollRepository _s49Repo;
        private readonly IRollDbConnectionFactory _rollConnectionFactory;
        private readonly INoticeQaService _qa;
        private readonly INoticeSourceStatusService _sourceStatus;

        private readonly IConfiguration _config;
        private readonly ILogger<NoticeBatchEmailService> _log;

        private static readonly TimeZoneInfo SouthAfricaTimeZone =
            GetSouthAfricaTimeZone();

        public NoticeBatchEmailService(
            AppDbContext db,
            IOptions<EmailOptions> emailOpt,
            IOptions<Section49Options> section49Options,
            IOptions<RollDbOptions> rollDbOptions,
            INoticeEmailTemplateService templates,
            INoticePathService paths,
            IS49RollRepository s49Repo,
            IRollDbConnectionFactory rollConnectionFactory,
            INoticeQaService qa,
            INoticeSourceStatusService sourceStatus,
            IConfiguration config,
            ILogger<NoticeBatchEmailService> log)
        {
            _db = db;
            _emailOpt = emailOpt.Value;
            _section49 = section49Options.Value;
            _rollDb = rollDbOptions.Value;

            _templates = templates;
            _paths = paths;
            _s49Repo = s49Repo;
            _rollConnectionFactory = rollConnectionFactory;
            _qa = qa;
            _sourceStatus = sourceStatus;

            _config = config;
            _log = log;
        }

        // ============================================================
        // PUBLIC API
        // ============================================================

        public async Task<int> CountSelectedRecordsAsync(
            IEnumerable<int> batchIds,
            CancellationToken ct)
        {
            var ids = batchIds
                .Distinct()
                .ToList();

            if (ids.Count == 0)
                return 0;

            return await _db.NoticeRunLogs
                .AsNoTracking()
                .CountAsync(
                    x =>
                        ids.Contains(x.NoticeBatchId) &&
                        x.Status == RunStatus.Printed &&
                        x.RecipientEmail != null &&
                        x.RecipientEmail != "" &&
                        x.PdfPath != null &&
                        x.PdfPath != "",
                    ct);
        }

        public async Task<SendBatchEmailResult> SendBatchEmailsAsync(
            IEnumerable<int> batchIds,
            Guid workflowKey,
            string sentBy,
            CancellationToken ct)
        {
            var ids = batchIds
                .Distinct()
                .ToList();

            if (ids.Count == 0)
            {
                return new SendBatchEmailResult
                {
                    ErrorMessage = "No batches selected."
                };
            }

            if (workflowKey == Guid.Empty)
            {
                return new SendBatchEmailResult
                {
                    ErrorMessage = "Workflow key is invalid."
                };
            }

            var settings =
                await _db.NoticeSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            x.ApprovalKey == workflowKey ||
                            x.WorkflowKey == workflowKey,
                        ct)
                ?? throw new InvalidOperationException(
                    "Workflow settings not found.");

            var roll =
                await _db.RollRegistry
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x => x.RollId == settings.RollId,
                        ct)
                ?? throw new InvalidOperationException(
                    "Roll not found.");

            var selectedBatches =
                await _db.NoticeBatches
                    .AsNoTracking()
                    .Where(x =>
                        ids.Contains(x.Id) &&
                        x.WorkflowKey == workflowKey &&
                        x.BatchKind == "STEP3")
                    .ToListAsync(ct);

            if (selectedBatches.Count != ids.Count)
            {
                return new SendBatchEmailResult
                {
                    ErrorMessage =
                        "One or more selected batches do not belong to this workflow."
                };
            }

            // ------------------------------------------------------------
            // QA GATE
            // ------------------------------------------------------------
            if (settings.Notice == NoticeKind.S49)
            {
                if (_section49.Qa.Enabled &&
                    _section49.Qa.RequireApprovalBeforeSend)
                {
                    var selectedApproved =
                        await AreSelectedS49BatchesQaApprovedAsync(
                            ids,
                            workflowKey,
                            ct);

                    if (!selectedApproved)
                    {
                        return new SendBatchEmailResult
                        {
                            ErrorMessage =
                                "Section 49 QA must be approved for every selected batch before notices can be sent."
                        };
                    }
                }
            }
            else
            {
                if (!await _qa.IsQaApprovedAsync(
                        workflowKey,
                        ct))
                {
                    return new SendBatchEmailResult
                    {
                        ErrorMessage =
                            "QA must be approved before notices can be sent."
                    };
                }
            }

            var batches =
                selectedBatches.ToDictionary(
                    x => x.Id);

            var result =
                new SendBatchEmailResult
                {
                    BatchesProcessed =
                        selectedBatches.Count
                };

            var maxSend =
                Math.Min(
                    HardMaxEmails,
                    _emailOpt.Limits?.MaxSendPerBatch
                        ?? HardMaxEmails);

            var delay =
                Math.Max(
                    0,
                    _emailOpt.Limits?.DelayMsBetweenSends
                        ?? 0);

            var readyLogs =
                await _db.NoticeRunLogs
                    .Where(x =>
                        ids.Contains(x.NoticeBatchId) &&
                        x.Status == RunStatus.Printed &&
                        x.RecipientEmail != null &&
                        x.RecipientEmail != "" &&
                        x.PdfPath != null &&
                        x.PdfPath != "")
                    .OrderBy(x => x.NoticeBatchId)
                    .ThenBy(x => x.Id)
                    .ToListAsync(ct);

            if (readyLogs.Count > maxSend)
            {
                result.ErrorMessage =
                    $"Selection contains {readyLogs.Count} emails but the limit is {maxSend}. " +
                    "Please select fewer batches.";

                return result;
            }

            // ------------------------------------------------------------
            // S53/S53Rev have companion-recipient workflow rules.
            // ------------------------------------------------------------
            if (settings.Notice.IsSection53Family())
            {
                await SendS53GroupsAsync(
                    settings,
                    roll,
                    batches,
                    readyLogs,
                    result,
                    sentBy,
                    delay,
                    ct);

                await MarkNoEmailLogsAsync(
                    settings,
                    roll,
                    ids,
                    result,
                    sentBy,
                    ct);

                return result;
            }

            // ------------------------------------------------------------
            // Normal batch send: S49, S51, S52, DJ, IN, S78, etc.
            // ------------------------------------------------------------
            foreach (var log in readyLogs)
            {
                ct.ThrowIfCancellationRequested();

                batches.TryGetValue(
                    log.NoticeBatchId,
                    out var batch);

                var batchName =
                    batch?.BatchName
                    ?? "Batch";

                var rollId =
                    batch?.RollId
                    ?? settings.RollId;

                try
                {
                    await SendNormalRunLogAsync(
                        settings,
                        roll,
                        log,
                        batchName,
                        rollId,
                        sentBy,
                        ct);

                    result.Sent++;
                }
                catch (Exception ex)
                {
                    await HandleNormalSendFailureAsync(
                        settings,
                        roll,
                        log,
                        batchName,
                        sentBy,
                        ex,
                        ct);

                    result.Failed++;
                }

                await _db.SaveChangesAsync(ct);

                if (delay > 0)
                    await Task.Delay(delay, ct);
            }

            await MarkNoEmailLogsAsync(
                settings,
                roll,
                ids,
                result,
                sentBy,
                ct);

            return result;
        }

        // ============================================================
        // NORMAL SEND
        // ============================================================

        private async Task SendNormalRunLogAsync(
            NoticeSettings settings,
            RollRegistry roll,
            NoticeRunLog log,
            string batchName,
            int rollId,
            string sentBy,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(log.PdfPath) ||
                !File.Exists(log.PdfPath))
            {
                throw new FileNotFoundException(
                    $"PDF not found for RunLog {log.Id}: {log.PdfPath}");
            }

            // Always resolve the current source email at send time.
            var freshEmail =
                await FetchFreshEmailAsync(
                    settings,
                    log,
                    batchName,
                    ct);

            var originalRecipient =
                !string.IsNullOrWhiteSpace(freshEmail)
                    ? freshEmail.Trim()
                    : log.RecipientEmail?.Trim();

            if (string.IsNullOrWhiteSpace(
                    originalRecipient))
            {
                throw new InvalidOperationException(
                    $"Recipient email is empty for RunLog {log.Id}.");
            }

            // Keep the original/client email in NoticeRunLog.
            log.RecipientEmail =
                originalRecipient;

            NoticeEmailRequest request;

            if (settings.Notice == NoticeKind.S49)
            {
                request =
                    await BuildS49EmailRequestAsync(
                        settings,
                        roll,
                        log,
                        originalRecipient,
                        ct);
            }
            else
            {
                request =
                    BuildEmailRequest(
                        settings,
                        roll,
                        log);

                request.RecipientEmail =
                    originalRecipient;
            }

            var (subject, bodyHtml) =
                _templates.Build(
                    request);

            var actualRecipient =
                ResolveActualRecipient(
                    originalRecipient);

            var isTestMode =
                IsTestModeEnabled();

            // ------------------------------------------------------------
            // S49: update the roll audit before SMTP starts.
            // ------------------------------------------------------------
            if (settings.Notice == NoticeKind.S49 &&
                !string.IsNullOrWhiteSpace(log.PremiseId))
            {
                await UpdateS49AuditAsync(
                    roll,
                    log.PremiseId,
                    batchName,
                    status: Section49Statuses.Sending,
                    originalEmail: originalRecipient,
                    actualSentTo: actualRecipient,
                    isTestMode: isTestMode,
                    emlPath: null,
                    sentBy: null,
                    sentDateUtc: null,
                    errorMessage: null,
                    ct: ct);
            }

            var emlPath =
                BuildEmlPath(
                    settings,
                    roll,
                    log,
                    batchName,
                    originalRecipient);

            emlPath =
                await SaveEmlAsync(
                    emlPath: emlPath,
                    toEmail: actualRecipient,
                    originalRecipient: originalRecipient,
                    subject: subject,
                    bodyHtml: bodyHtml,
                    pdfPath: log.PdfPath,
                    ct: ct,
                    ccAddress:
                        string.IsNullOrWhiteSpace(
                            _emailOpt.CcAddress)
                            ? null
                            : _emailOpt.CcAddress.Trim(),
                    fromAddress:
                        string.IsNullOrWhiteSpace(
                            _emailOpt.FromAddress)
                            ? null
                            : _emailOpt.FromAddress.Trim(),
                    fromName:
                        string.IsNullOrWhiteSpace(
                            _emailOpt.FromName)
                            ? null
                            : _emailOpt.FromName.Trim());

            log.EmlPath =
                emlPath;

            // IMPORTANT:
            // SMTP exceptions are NOT swallowed.
            // A send is only marked Sent after this method completes.
            await SendOneEmailAsync(
                subject,
                bodyHtml,
                actualRecipient,
                log.PdfPath,
                BuildTrackingReference(
                    settings,
                    log),
                ct);

            log.Status =
                RunStatus.Sent;

            log.SentAtUtc =
                DateTime.UtcNow;

            log.SentBy =
                sentBy;

            log.ErrorMessage =
                null;

            if (settings.Notice == NoticeKind.S49 &&
                !string.IsNullOrWhiteSpace(log.PremiseId))
            {
                if (UsesS49EmailAvailabilityMode(
                        roll))
                {
                    await UpdateS49AuditAsync(
                        roll,
                        log.PremiseId,
                        batchName,
                        status: Section49Statuses.Sent,
                        originalEmail: originalRecipient,
                        actualSentTo: actualRecipient,
                        isTestMode: isTestMode,
                        emlPath: emlPath,
                        sentBy: sentBy,
                        sentDateUtc: DateTime.UtcNow,
                        errorMessage: null,
                        ct: ct);
                }
                else
                {
                    await _s49Repo.MarkEmailSentAsync(
                        rollId,
                        log.PremiseId,
                        ct);
                }
            }

            _log.LogInformation(
                "SMTP sent. Notice={Notice}, RunLogId={RunLogId}, OriginalRecipient={OriginalRecipient}, ActualRecipient={ActualRecipient}, TestMode={TestMode}",
                settings.Notice,
                log.Id,
                originalRecipient,
                actualRecipient,
                isTestMode);
        }

        private async Task HandleNormalSendFailureAsync(
            NoticeSettings settings,
            RollRegistry roll,
            NoticeRunLog log,
            string batchName,
            string sentBy,
            Exception ex,
            CancellationToken ct)
        {
            _log.LogError(
                ex,
                "Email send failed. Notice={Notice}, RunLogId={RunLogId}, ObjectionNo={ObjectionNo}, AppealNo={AppealNo}, PremiseId={PremiseId}, Batch={BatchName}",
                settings.Notice,
                log.Id,
                log.ObjectionNo,
                log.AppealNo,
                log.PremiseId,
                batchName);

            var error =
                LimitError(
                    ex.Message);

            log.Status =
                RunStatus.Failed;

            log.ErrorMessage =
                error;

            log.SentBy =
                sentBy;

            if (settings.Notice == NoticeKind.S49 &&
                !string.IsNullOrWhiteSpace(log.PremiseId))
            {
                try
                {
                    var original =
                        log.RecipientEmail?.Trim();

                    var actual =
                        string.IsNullOrWhiteSpace(original)
                            ? null
                            : ResolveActualRecipient(
                                original);

                    if (UsesS49EmailAvailabilityMode(
                            roll))
                    {
                        await UpdateS49AuditAsync(
                            roll,
                            log.PremiseId,
                            batchName,
                            status: Section49Statuses.Failed,
                            originalEmail: original,
                            actualSentTo: actual,
                            isTestMode: IsTestModeEnabled(),
                            emlPath: log.EmlPath,
                            sentBy: sentBy,
                            sentDateUtc: null,
                            errorMessage: error,
                            ct: ct);
                    }
                    else
                    {
                        await _s49Repo.MarkEmailFailedAsync(
                            roll.RollId,
                            log.PremiseId,
                            ct);
                    }
                }
                catch (Exception auditEx)
                {
                    _log.LogError(
                        auditEx,
                        "Failed to update Section 49 audit after email failure. PremiseId={PremiseId}, Batch={BatchName}",
                        log.PremiseId,
                        batchName);
                }
            }
        }

        // ============================================================
        // S53 / S53 REVISED
        // ============================================================

        private async Task SendS53GroupsAsync(
            NoticeSettings settings,
            RollRegistry roll,
            IReadOnlyDictionary<int, NoticeBatch> batches,
            List<NoticeRunLog> runLogs,
            SendBatchEmailResult result,
            string sentBy,
            int delay,
            CancellationToken ct)
        {
            var groups =
                runLogs
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(
                            x.ObjectionNo))
                    .GroupBy(
                        x => x.ObjectionNo!.Trim(),
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x.Key)
                    .ToList();

            foreach (var group in groups)
            {
                ct.ThrowIfCancellationRequested();

                var objectionNo =
                    group.Key;

                var groupLogs =
                    group
                        .OrderBy(x => x.Id)
                        .ToList();

                batches.TryGetValue(
                    groupLogs[0].NoticeBatchId,
                    out var batch);

                var batchName =
                    batch?.BatchName
                    ?? "Batch";

                var rollId =
                    batch?.RollId
                    ?? settings.RollId;

                try
                {
                    var canSend =
                        await _sourceStatus.IsS53StatusAsync(
                            rollId,
                            objectionNo,
                            NoticeWorkflowStatus.EmailSentPending,
                            ct);

                    if (!canSend)
                    {
                        throw new InvalidOperationException(
                            $"Cannot send S53 notice {objectionNo}. " +
                            $"Source status must be '{NoticeWorkflowStatus.EmailSentPending}'.");
                    }

                    foreach (var log in groupLogs)
                    {
                        try
                        {
                            var objectorType =
                                string.IsNullOrWhiteSpace(
                                    log.RecipientName)
                                    ? "Owner"
                                    : log.RecipientName.Trim();

                            var source =
                                await FetchS53MvdEmailDataAsync(
                                    settings.Notice,
                                    rollId,
                                    objectionNo,
                                    batchName,
                                    objectorType,
                                    ct)
                                ?? throw new InvalidOperationException(
                                    $"S53 source row not found for ObjectionNo={objectionNo}, ObjectorType={objectorType}, Batch={batchName}.");

                            if (string.IsNullOrWhiteSpace(
                                    source.Email))
                            {
                                throw new InvalidOperationException(
                                    $"S53 email is empty for ObjectionNo={objectionNo}, ObjectorType={objectorType}.");
                            }

                            if (string.IsNullOrWhiteSpace(
                                    log.PdfPath) ||
                                !File.Exists(
                                    log.PdfPath))
                            {
                                throw new FileNotFoundException(
                                    $"S53 PDF was not found for RunLog {log.Id}: {log.PdfPath}");
                            }

                            var originalRecipient =
                                source.Email.Trim();

                            log.RecipientEmail =
                                originalRecipient;

                            if (!string.IsNullOrWhiteSpace(
                                    source.PropertyDesc))
                            {
                                log.PropertyDesc =
                                    source.PropertyDesc.Trim();
                            }

                            var request =
                                BuildEmailRequest(
                                    settings,
                                    roll,
                                    log);

                            request.RecipientEmail =
                                originalRecipient;

                            if (request.Items.Count > 0)
                            {
                                request.Items[0].PropertyDesc =
                                    source.PropertyDesc
                                    ?? log.PropertyDesc
                                    ?? "";

                                request.Items[0].ObjectionNo =
                                    source.ObjectionNo
                                    ?? objectionNo;
                            }

                            var (subject, bodyHtml) =
                                _templates.Build(
                                    request);

                            var actualRecipient =
                                ResolveActualRecipient(
                                    originalRecipient);

                            var emlPath =
                                BuildEmlPathNextToPdf(
                                    log.PdfPath,
                                    settings.Notice,
                                    log.ObjectionNo,
                                    log.AppealNo,
                                    log.PropertyDesc,
                                    GetArchiveRecipient(
                                        originalRecipient,
                                        actualRecipient));

                            emlPath =
                                await SaveEmlAsync(
                                    emlPath,
                                    actualRecipient,
                                    originalRecipient,
                                    subject,
                                    bodyHtml,
                                    log.PdfPath,
                                    ct,
                                    ccAddress:
                                        string.IsNullOrWhiteSpace(
                                            _emailOpt.CcAddress)
                                            ? null
                                            : _emailOpt.CcAddress.Trim(),
                                    fromAddress:
                                        string.IsNullOrWhiteSpace(
                                            _emailOpt.FromAddress)
                                            ? null
                                            : _emailOpt.FromAddress.Trim(),
                                    fromName:
                                        string.IsNullOrWhiteSpace(
                                            _emailOpt.FromName)
                                            ? null
                                            : _emailOpt.FromName.Trim());

                            log.EmlPath =
                                emlPath;

                            await SendOneEmailAsync(
                                subject,
                                bodyHtml,
                                actualRecipient,
                                log.PdfPath,
                                BuildTrackingReference(
                                    settings,
                                    log),
                                ct);

                            log.Status =
                                RunStatus.Sent;

                            log.SentAtUtc =
                                DateTime.UtcNow;

                            log.SentBy =
                                sentBy;

                            log.ErrorMessage =
                                null;

                            result.Sent++;

                            await _db.SaveChangesAsync(ct);

                            if (delay > 0)
                                await Task.Delay(delay, ct);
                        }
                        catch (Exception ex)
                        {
                            _log.LogError(
                                ex,
                                "S53 email send failed. RunLogId={RunLogId}, ObjectionNo={ObjectionNo}, Batch={BatchName}",
                                log.Id,
                                objectionNo,
                                batchName);

                            log.Status =
                                RunStatus.Failed;

                            log.ErrorMessage =
                                LimitError(
                                    ex.Message);

                            log.SentBy =
                                sentBy;

                            result.Failed++;

                            await _db.SaveChangesAsync(ct);
                        }
                    }

                    if (groupLogs.All(
                            x => x.Status == RunStatus.Sent))
                    {
                        await _sourceStatus.SetS53StatusAsync(
                            rollId,
                            new[] { objectionNo },
                            NoticeWorkflowStatus.NoticeSent,
                            ct);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogError(
                        ex,
                        "S53 email group failed. ObjectionNo={ObjectionNo}, Batch={BatchName}",
                        objectionNo,
                        batchName);

                    foreach (var log in groupLogs)
                    {
                        if (log.Status == RunStatus.Sent)
                            continue;

                        log.Status =
                            RunStatus.Failed;

                        log.ErrorMessage =
                            LimitError(
                                ex.Message);

                        log.SentBy =
                            sentBy;
                    }

                    await _db.SaveChangesAsync(ct);
                }
            }

            var missingObjectionLogs =
                runLogs
                    .Where(x =>
                        string.IsNullOrWhiteSpace(
                            x.ObjectionNo))
                    .ToList();

            foreach (var log in missingObjectionLogs)
            {
                log.Status =
                    RunStatus.Failed;

                log.ErrorMessage =
                    $"S53 RunLog {log.Id} has no ObjectionNo.";

                log.SentBy =
                    sentBy;

                result.Failed++;
            }

            if (missingObjectionLogs.Count > 0)
                await _db.SaveChangesAsync(ct);
        }

        // ============================================================
        // NO EMAIL
        // ============================================================

        private async Task MarkNoEmailLogsAsync(
            NoticeSettings settings,
            RollRegistry roll,
            IReadOnlyCollection<int> batchIds,
            SendBatchEmailResult result,
            string sentBy,
            CancellationToken ct)
        {
            var noEmailLogs =
                await _db.NoticeRunLogs
                    .Where(x =>
                        batchIds.Contains(
                            x.NoticeBatchId) &&
                        x.Status == RunStatus.Printed &&
                        (x.RecipientEmail == null ||
                         x.RecipientEmail == ""))
                    .ToListAsync(ct);

            foreach (var log in noEmailLogs)
            {
                log.Status =
                    RunStatus.NoEmail;

                log.SentBy =
                    sentBy;

                log.ErrorMessage =
                    "Recipient email is empty.";

                result.Skipped++;

                if (settings.Notice == NoticeKind.S49 &&
                    !string.IsNullOrWhiteSpace(
                        log.PremiseId) &&
                    UsesS49EmailAvailabilityMode(
                        roll))
                {
                    try
                    {
                        var batchName =
                            await _db.NoticeBatches
                                .AsNoTracking()
                                .Where(x =>
                                    x.Id == log.NoticeBatchId)
                                .Select(x =>
                                    x.BatchName)
                                .FirstOrDefaultAsync(ct)
                            ?? "";

                        await UpdateS49AuditAsync(
                            roll,
                            log.PremiseId,
                            batchName,
                            Section49Statuses.NoEmail,
                            originalEmail: null,
                            actualSentTo: null,
                            isTestMode: false,
                            emlPath: null,
                            sentBy: sentBy,
                            sentDateUtc: null,
                            errorMessage:
                                "Recipient email is empty.",
                            ct: ct);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(
                            ex,
                            "Failed to update S49 NoEmail audit for PremiseId={PremiseId}.",
                            log.PremiseId);
                    }
                }
            }

            if (noEmailLogs.Count > 0)
                await _db.SaveChangesAsync(ct);
        }

        // ============================================================
        // S49 AUDIT
        // ============================================================

        private bool UsesS49EmailAvailabilityMode(
            RollRegistry roll)
        {
            if (string.IsNullOrWhiteSpace(
                    roll.SourceDb))
            {
                return false;
            }

            var source =
                _rollDb.GetSource(
                    roll.SourceDb.Trim());

            return string.Equals(
                source.Section49?.EmailSentMode,
                RollSection49EmailSentModes.EmailAvailability,
                StringComparison.OrdinalIgnoreCase);
        }

        private async Task UpdateS49AuditAsync(
            RollRegistry roll,
            string premiseId,
            string batchName,
            string status,
            string? originalEmail,
            string? actualSentTo,
            bool isTestMode,
            string? emlPath,
            string? sentBy,
            DateTime? sentDateUtc,
            string? errorMessage,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(
                    roll.SourceDb))
            {
                return;
            }

            var sourceDb =
                roll.SourceDb.Trim();

            var source =
                _rollDb.GetSource(
                    sourceDb);

            if (source.Section49 == null ||
                !source.Section49.HasAuditTable)
            {
                return;
            }

            var auditTable =
                QuoteSqlIdentifier(
                    source.Section49.AuditTable);

            var sql = $"""
                UPDATE dbo.{auditTable}
                SET
                    Eml_Path =
                        CASE
                            WHEN @EmlPath IS NULL
                            THEN Eml_Path
                            ELSE @EmlPath
                        END,

                    Original_Email_Addr =
                        CASE
                            WHEN @OriginalEmail IS NULL
                            THEN Original_Email_Addr
                            ELSE @OriginalEmail
                        END,

                    Actual_Sent_To =
                        @ActualSentTo,

                    Is_Test_Mode =
                        @IsTestMode,

                    Sent_Date =
                        @SentDate,

                    Sent_By =
                        @SentBy,

                    Send_Status =
                        @Status,

                    Error_Message =
                        @ErrorMessage

                WHERE
                    PREMISE_ID = @PremiseId
                    AND Batch_Name = @BatchName;
                """;

            await using var cn =
                _rollConnectionFactory.Create(
                    sourceDb);

            await cn.OpenAsync(ct);

            await using var cmd =
                new SqlCommand(
                    sql,
                    cn)
                {
                    CommandTimeout = 60
                };

            cmd.Parameters.Add(
                new SqlParameter(
                    "@PremiseId",
                    SqlDbType.VarChar,
                    50)
                {
                    Value = premiseId
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@BatchName",
                    SqlDbType.NVarChar,
                    100)
                {
                    Value = batchName
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@Status",
                    SqlDbType.NVarChar,
                    50)
                {
                    Value = status
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@OriginalEmail",
                    SqlDbType.NVarChar,
                    320)
                {
                    Value = DbValue(
                        originalEmail)
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@ActualSentTo",
                    SqlDbType.NVarChar,
                    320)
                {
                    Value = DbValue(
                        actualSentTo)
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@IsTestMode",
                    SqlDbType.Bit)
                {
                    Value = isTestMode
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@EmlPath",
                    SqlDbType.NVarChar,
                    2000)
                {
                    Value = DbValue(
                        emlPath)
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@SentBy",
                    SqlDbType.NVarChar,
                    150)
                {
                    Value = DbValue(
                        sentBy)
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@SentDate",
                    SqlDbType.DateTime2)
                {
                    Value =
                        sentDateUtc.HasValue
                            ? sentDateUtc.Value
                            : DBNull.Value
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@ErrorMessage",
                    SqlDbType.NVarChar,
                    2000)
                {
                    Value = DbValue(
                        errorMessage)
                });

            await cmd.ExecuteNonQueryAsync(ct);
        }

        private async Task<bool> AreSelectedS49BatchesQaApprovedAsync(
            IReadOnlyCollection<int> batchIds,
            Guid workflowKey,
            CancellationToken ct)
        {
            var runLogRows =
                await _db.NoticeRunLogs
                    .AsNoTracking()
                    .Where(x =>
                        batchIds.Contains(
                            x.NoticeBatchId))
                    .Select(x => new
                    {
                        x.Id,
                        x.NoticeBatchId
                    })
                    .ToListAsync(ct);

            if (runLogRows.Count == 0)
                return false;

            var approvedRuns =
                await _db.NoticeQaRuns
                    .AsNoTracking()
                    .Include(x => x.Items)
                    .Where(x =>
                        x.WorkflowKey == workflowKey &&
                        x.Notice == NoticeKind.S49 &&
                        x.Status == "Approved")
                    .ToListAsync(ct);

            foreach (var batchId in batchIds)
            {
                var idsForBatch =
                    runLogRows
                        .Where(x =>
                            x.NoticeBatchId == batchId)
                        .Select(x =>
                            x.Id)
                        .ToHashSet();

                if (idsForBatch.Count == 0)
                    return false;

                var approvedForBatch =
                    approvedRuns.Any(run =>
                        run.Items.Any(item =>
                            item.NoticeRunLogId.HasValue &&
                            idsForBatch.Contains(
                                item.NoticeRunLogId.Value)));

                if (!approvedForBatch)
                    return false;
            }

            return true;
        }

        // ============================================================
        // EMAIL REQUEST BUILDERS
        // ============================================================

        private static NoticeEmailRequest BuildEmailRequest(
            NoticeSettings settings,
            RollRegistry roll,
            NoticeRunLog log)
        {
            var request =
                new NoticeEmailRequest
                {
                    Notice =
                        settings.Notice,

                    RollShortCode =
                        roll.ShortCode
                        ?? "",

                    RollName =
                        roll.Name
                        ?? "",

                    RecipientName =
                        log.RecipientName
                        ?? "",

                    RecipientEmail =
                        log.RecipientEmail
                        ?? "",

                    FinancialYearsText =
                        settings.FinancialYearsText,

                    IsSection52Review =
                        settings.IsSection52Review,

                    InvalidKind =
                        settings.IsInvalidOmission == true
                            ? InvalidNoticeKind.InvalidOmission
                            : InvalidNoticeKind.InvalidObjection,

                    Items =
                        new List<NoticeEmailPropertyItem>
                        {
                            new()
                            {
                                PropertyDesc =
                                    log.PropertyDesc
                                    ?? "",

                                ObjectionNo =
                                    log.ObjectionNo,

                                AppealNo =
                                    log.AppealNo
                            }
                        }
                };

            if (settings.Notice ==
                NoticeKind.S49)
            {
                request.InspectionStart =
                    settings.ObjectionStartDate.HasValue
                        ? DateOnly.FromDateTime(
                            settings.ObjectionStartDate.Value)
                        : null;

                request.InspectionEnd =
                    settings.ObjectionEndDate.HasValue
                        ? DateOnly.FromDateTime(
                            settings.ObjectionEndDate.Value)
                        : null;

                request.ExtendedEnd =
                    settings.ExtensionDate.HasValue
                        ? DateOnly.FromDateTime(
                            settings.ExtensionDate.Value)
                        : null;
            }

            return request;
        }

        private async Task<NoticeEmailRequest> BuildS49EmailRequestAsync(
            NoticeSettings settings,
            RollRegistry roll,
            NoticeRunLog log,
            string originalRecipient,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(
                    log.PremiseId))
            {
                throw new InvalidOperationException(
                    $"S49 RunLog {log.Id} has no PremiseId.");
            }

            var (rows, contact) =
                await _s49Repo.LoadPremiseAsync(
                    roll.RollId,
                    log.PremiseId,
                    ct);

            if (rows.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No S49 roll data found for PremiseId {log.PremiseId}.");
            }

            var firstRow =
                rows[0];

            var propertyDesc =
                firstRow.PropertyDesc?.Trim()
                ?? "";

            if (string.IsNullOrWhiteSpace(
                    propertyDesc))
            {
                throw new InvalidOperationException(
                    $"Property description is empty for S49 PremiseId {log.PremiseId}.");
            }

            log.PropertyDesc =
                propertyDesc;

            return new NoticeEmailRequest
            {
                Notice =
                    NoticeKind.S49,

                RollShortCode =
                    roll.ShortCode
                    ?? "",

                RollName =
                    roll.Name
                    ?? "",

                RollDisplayName =
                    roll.Name
                    ?? "",

                RecipientName =
                    contact?.Addr1?.Trim()
                    ?? "Property Owner",

                // Keep the template request based on the client/original
                // email. SMTP routing is resolved separately.
                RecipientEmail =
                    originalRecipient,

                IsMulti =
                    rows.Count > 1,

                FinancialYearsText =
                    settings.FinancialYearsText,

                InspectionStart =
                    settings.ObjectionStartDate.HasValue
                        ? DateOnly.FromDateTime(
                            settings.ObjectionStartDate.Value)
                        : null,

                InspectionEnd =
                    settings.ObjectionEndDate.HasValue
                        ? DateOnly.FromDateTime(
                            settings.ObjectionEndDate.Value)
                        : null,

                ExtendedEnd =
                    settings.ExtensionDate.HasValue
                        ? DateOnly.FromDateTime(
                            settings.ExtensionDate.Value)
                        : null,

                Items =
                    new List<NoticeEmailPropertyItem>
                    {
                        new()
                        {
                            PropertyDesc =
                                propertyDesc
                        }
                    }
            };
        }

        // ============================================================
        // TEST MODE / RECIPIENT RESOLUTION
        // ============================================================

        private bool IsTestModeEnabled()
        {
            return _emailOpt.TestMode?.Enabled
                ?? false;
        }

        private string ResolveActualRecipient(
            string originalRecipient)
        {
            if (!IsTestModeEnabled())
                return originalRecipient.Trim();

            var testRecipient =
                _emailOpt.TestMode?.Recipient
                    ?.Trim();

            if (string.IsNullOrWhiteSpace(
                    testRecipient))
            {
                throw new InvalidOperationException(
                    "Email TestMode is enabled but Email:TestMode:Recipient is empty.");
            }

            return testRecipient;
        }

        private string GetArchiveRecipient(
            string originalRecipient,
            string actualRecipient)
        {
            if (!IsTestModeEnabled())
                return originalRecipient;

            if (_emailOpt.TestMode?
                    .UseOriginalRecipientInArchiveFileName
                ?? true)
            {
                return originalRecipient;
            }

            return actualRecipient;
        }

        // ============================================================
        // EML PATHS
        // ============================================================

        private string BuildEmlPath(
            NoticeSettings settings,
            RollRegistry roll,
            NoticeRunLog log,
            string batchName,
            string originalRecipient)
        {
            var actualRecipient =
                ResolveActualRecipient(
                    originalRecipient);

            var archiveRecipient =
                GetArchiveRecipient(
                    originalRecipient,
                    actualRecipient);

            if (settings.Notice ==
                NoticeKind.S49)
            {
                var configured =
                    TryBuildConfiguredS49EmlPath(
                        roll,
                        log.PropertyDesc
                            ?? log.PremiseId
                            ?? "Property",
                        archiveRecipient);

                if (!string.IsNullOrWhiteSpace(
                        configured))
                {
                    return configured;
                }
            }

            if (!string.IsNullOrWhiteSpace(
                    log.PdfPath))
            {
                return BuildEmlPathNextToPdf(
                    log.PdfPath,
                    settings.Notice,
                    log.ObjectionNo,
                    log.AppealNo,
                    log.PropertyDesc,
                    archiveRecipient);
            }

            var folderKey =
                log.PropertyDesc
                ?? log.ObjectionNo
                ?? log.AppealNo
                ?? log.PremiseId
                ?? log.Id.ToString();

            var oldPath =
                _paths.BuildBatchEmlPath(
                    roll,
                    settings.Notice,
                    batchName,
                    folderKey);

            var folder =
                Path.GetDirectoryName(
                    oldPath)
                ?? throw new InvalidOperationException(
                    "Could not resolve EML folder.");

            Directory.CreateDirectory(
                folder);

            return Path.Combine(
                folder,
                BuildSentNoticeEmlFileName(
                    settings.Notice,
                    log.ObjectionNo,
                    log.AppealNo,
                    log.PropertyDesc,
                    archiveRecipient));
        }

        private string? TryBuildConfiguredS49EmlPath(
            RollRegistry roll,
            string propertyDesc,
            string originalEmail)
        {
            if (string.IsNullOrWhiteSpace(
                    roll.SourceDb))
            {
                return null;
            }

            var source =
                _rollDb.GetSource(
                    roll.SourceDb.Trim());

            var root =
                source.Section49?
                    .Storage?
                    .EmailRootPath
                    ?.Trim();

            if (string.IsNullOrWhiteSpace(
                    root))
            {
                return null;
            }

            var safeProperty =
                MakeSafeFilePart(
                    propertyDesc);

            if (string.IsNullOrWhiteSpace(
                    safeProperty))
            {
                safeProperty =
                    "Property";
            }

            var safeEmail =
                MakeSafeFilePart(
                    originalEmail);

            if (string.IsNullOrWhiteSpace(
                    safeEmail))
            {
                safeEmail =
                    "NoEmail";
            }

            var pattern =
                _section49
                    .StorageDefaults
                    .EmlFileNamePattern;

            if (string.IsNullOrWhiteSpace(
                    pattern))
            {
                pattern =
                    "email_{PropertyDesc}_{OriginalEmail}.eml";
            }

            var fileName =
                pattern
                    .Replace(
                        "{PropertyDesc}",
                        safeProperty,
                        StringComparison.OrdinalIgnoreCase)
                    .Replace(
                        "{OriginalEmail}",
                        safeEmail,
                        StringComparison.OrdinalIgnoreCase);

            fileName =
                MakeSafeFilePartWithExtension(
                    fileName,
                    ".eml");

            if (_section49
                    .StorageDefaults
                    .CreatePropertyFolderForEmail)
            {
                return Path.Combine(
                    root,
                    safeProperty,
                    fileName);
            }

            return Path.Combine(
                root,
                fileName);
        }

        private static string BuildEmlPathNextToPdf(
            string pdfPath,
            NoticeKind notice,
            string? objectionNo,
            string? appealNo,
            string? propertyDesc,
            string? recipientEmail)
        {
            var folder =
                Path.GetDirectoryName(
                    pdfPath);

            if (string.IsNullOrWhiteSpace(
                    folder))
            {
                throw new InvalidOperationException(
                    $"Invalid PDF path: {pdfPath}");
            }

            Directory.CreateDirectory(
                folder);

            return Path.Combine(
                folder,
                BuildSentNoticeEmlFileName(
                    notice,
                    objectionNo,
                    appealNo,
                    propertyDesc,
                    recipientEmail));
        }

        private static string BuildSentNoticeEmlFileName(
            NoticeKind notice,
            string? objectionNo,
            string? appealNo,
            string? propertyDesc,
            string? recipientEmail)
        {
            var stamp =
                SouthAfricaNow()
                    .ToString(
                        "yyyyMMdd_HHmmss");

            var email =
                MakeSafeFilePart(
                    recipientEmail);

            if (string.IsNullOrWhiteSpace(
                    email))
            {
                email =
                    "NoEmail";
            }

            if (notice ==
                NoticeKind.S53Rev)
            {
                var key =
                    MakeSafeFilePart(
                        objectionNo);

                if (string.IsNullOrWhiteSpace(
                        key))
                {
                    key =
                        MakeSafeFilePart(
                            propertyDesc);
                }

                if (string.IsNullOrWhiteSpace(
                        key))
                {
                    key =
                        "Notice";
                }

                return
                    $"email_{key}_{email}_RevisedMVD_{stamp}.eml";
            }

            string normalKey;

            if (notice ==
                NoticeKind.S49)
            {
                normalKey =
                    MakeSafeFilePart(
                        propertyDesc);

                if (string.IsNullOrWhiteSpace(
                        normalKey))
                {
                    normalKey =
                        "Property";
                }
            }
            else
            {
                normalKey =
                    MakeSafeFilePart(
                        objectionNo);

                if (string.IsNullOrWhiteSpace(
                        normalKey))
                {
                    normalKey =
                        MakeSafeFilePart(
                            appealNo);
                }

                if (string.IsNullOrWhiteSpace(
                        normalKey))
                {
                    normalKey =
                        MakeSafeFilePart(
                            propertyDesc);
                }

                if (string.IsNullOrWhiteSpace(
                        normalKey))
                {
                    normalKey =
                        "Notice";
                }
            }

            return
                $"email_{normalKey}_{email}_{stamp}.eml";
        }

        // ============================================================
        // SAVE .EML
        // ============================================================

        private static async Task<string> SaveEmlAsync(
            string emlPath,
            string toEmail,
            string? originalRecipient,
            string subject,
            string bodyHtml,
            string pdfPath,
            CancellationToken ct,
            string? ccAddress = null,
            string? fromAddress = null,
            string? fromName = null)
        {
            var folderPath =
                Path.GetDirectoryName(
                    emlPath);

            if (string.IsNullOrWhiteSpace(
                    folderPath))
            {
                throw new InvalidOperationException(
                    $"Invalid EML path: {emlPath}");
            }

            Directory.CreateDirectory(
                folderPath);

            if (string.IsNullOrWhiteSpace(
                    toEmail))
            {
                throw new InvalidOperationException(
                    "Recipient email is empty. Cannot save EML copy.");
            }

            if (string.IsNullOrWhiteSpace(
                    pdfPath) ||
                !File.Exists(
                    pdfPath))
            {
                throw new FileNotFoundException(
                    $"PDF not found: {pdfPath}");
            }

            var pdfBytes =
                await File.ReadAllBytesAsync(
                    pdfPath,
                    ct);

            var pdfB64 =
                Convert.ToBase64String(
                    pdfBytes);

            var pdfName =
                Path.GetFileName(
                    pdfPath);

            var boundary =
                $"----=_Part_{Guid.NewGuid():N}";

            var bodyB64 =
                Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(
                        bodyHtml
                        ?? ""));

            var now =
                SouthAfricaNow()
                    .ToString(
                        "ddd, dd MMM yyyy HH:mm:ss zzz",
                        System.Globalization
                            .CultureInfo
                            .InvariantCulture);

            var safeSubject =
                string.IsNullOrWhiteSpace(
                    subject)
                    ? "Notice"
                    : subject
                        .Replace("\r", " ")
                        .Replace("\n", " ")
                        .Trim();

            var sb =
                new StringBuilder();

            if (!string.IsNullOrWhiteSpace(
                    fromName) &&
                !string.IsNullOrWhiteSpace(
                    fromAddress))
            {
                sb.AppendLine(
                    $"From: {EncodeMailHeader(fromName)} <{fromAddress}>");
            }
            else if (!string.IsNullOrWhiteSpace(
                         fromAddress))
            {
                sb.AppendLine(
                    $"From: {fromAddress}");
            }

            sb.AppendLine(
                $"Date: {now}");

            sb.AppendLine(
                $"To: {toEmail}");

            if (!string.IsNullOrWhiteSpace(
                    originalRecipient) &&
                !string.Equals(
                    originalRecipient,
                    toEmail,
                    StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine(
                    $"X-Original-Recipient: {EncodeMailHeader(originalRecipient)}");
            }

            if (!string.IsNullOrWhiteSpace(
                    ccAddress))
            {
                sb.AppendLine(
                    $"Cc: {ccAddress}");
            }

            sb.AppendLine(
                $"Subject: {EncodeMailHeader(safeSubject)}");

            sb.AppendLine(
                "MIME-Version: 1.0");

            sb.AppendLine(
                $"Content-Type: multipart/mixed; boundary=\"{boundary}\"");

            sb.AppendLine();
            sb.AppendLine(
                $"--{boundary}");

            sb.AppendLine(
                "Content-Type: text/html; charset=utf-8");

            sb.AppendLine(
                "Content-Transfer-Encoding: base64");

            sb.AppendLine();

            WriteBase64Lines(
                sb,
                bodyB64);

            sb.AppendLine();
            sb.AppendLine(
                $"--{boundary}");

            sb.AppendLine(
                $"Content-Type: application/pdf; name=\"{pdfName}\"");

            sb.AppendLine(
                "Content-Transfer-Encoding: base64");

            sb.AppendLine(
                $"Content-Disposition: attachment; filename=\"{pdfName}\"");

            sb.AppendLine();

            WriteBase64Lines(
                sb,
                pdfB64);

            sb.AppendLine();
            sb.AppendLine(
                $"--{boundary}--");

            if (File.Exists(
                    emlPath))
            {
                var name =
                    Path.GetFileNameWithoutExtension(
                        emlPath);

                var ext =
                    Path.GetExtension(
                        emlPath);

                emlPath =
                    Path.Combine(
                        folderPath,
                        $"{name}_{Guid.NewGuid():N}{ext}");
            }

            await File.WriteAllTextAsync(
                emlPath,
                sb.ToString(),
                Encoding.UTF8,
                ct);

            return emlPath;
        }

        // ============================================================
        // SMTP
        // ============================================================

        private async Task SendOneEmailAsync(
            string subject,
            string bodyHtml,
            string actualRecipient,
            string pdfPath,
            string trackingReference,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(
                    pdfPath) ||
                !File.Exists(
                    pdfPath))
            {
                throw new FileNotFoundException(
                    $"PDF not found: {pdfPath}");
            }

            if (string.IsNullOrWhiteSpace(
                    actualRecipient))
            {
                throw new InvalidOperationException(
                    "Actual email recipient is empty.");
            }

            if (string.IsNullOrWhiteSpace(
                    _emailOpt.Smtp?.Host))
            {
                throw new InvalidOperationException(
                    "Email:Smtp:Host is not configured.");
            }

            if (string.IsNullOrWhiteSpace(
                    _emailOpt.FromAddress))
            {
                throw new InvalidOperationException(
                    "Email:FromAddress is not configured.");
            }

            using var message =
                new MailMessage
                {
                    From =
                        new MailAddress(
                            _emailOpt.FromAddress.Trim(),
                            _emailOpt.FromName
                                ?? ""),

                    Subject =
                        subject,

                    Body =
                        bodyHtml,

                    IsBodyHtml =
                        true
                };

            message.To.Add(
                new MailAddress(
                    actualRecipient.Trim()));

            if (!string.IsNullOrWhiteSpace(
                    _emailOpt.CcAddress))
            {
                message.CC.Add(
                    new MailAddress(
                        _emailOpt.CcAddress.Trim(),
                        string.IsNullOrWhiteSpace(
                            _emailOpt.CcName)
                            ? ""
                            : _emailOpt.CcName.Trim()));
            }

            var pdfBytes =
                await File.ReadAllBytesAsync(
                    pdfPath,
                    ct);

            message.Attachments.Add(
                new Attachment(
                    new MemoryStream(
                        pdfBytes),
                    Path.GetFileName(
                        pdfPath),
                    "application/pdf"));

            // Existing shared delivery/read receipt implementation.
            OutboundEmailTracking.Apply(
                message,
                _config,
                trackingReference);

            using var smtp =
                new SmtpClient(
                    _emailOpt.Smtp.Host,
                    _emailOpt.Smtp.Port)
                {
                    EnableSsl =
                        _emailOpt.Smtp.EnableSsl
                };

            var useDefaultCredentials =
                _emailOpt.Smtp.UseDefaultCredentials ||
                _emailOpt.UseDefaultCredentials;

            smtp.UseDefaultCredentials =
                useDefaultCredentials;

            if (!useDefaultCredentials &&
                !string.IsNullOrWhiteSpace(
                    _emailOpt.Smtp.Username))
            {
                smtp.Credentials =
                    new NetworkCredential(
                        _emailOpt.Smtp.Username,
                        _emailOpt.Smtp.Password
                        ?? "");
            }

            // Do NOT catch SmtpException here.
            // Caller must receive the failure and mark the row Failed.
            await Task.Run(
                () => smtp.Send(message),
                ct);
        }

        // ============================================================
        // SOURCE EMAIL LOOKUPS
        // ============================================================

        private async Task<string?> FetchFreshEmailAsync(
            NoticeSettings settings,
            NoticeRunLog log,
            string batchName,
            CancellationToken ct)
        {
            return settings.Notice switch
            {
                NoticeKind.S49 =>
                    await FetchS49EmailAsync(
                        settings.RollId,
                        log.PremiseId,
                        ct),

                NoticeKind.S51 =>
                    await FetchS51EmailAsync(
                        settings.RollId,
                        log.ObjectionNo,
                        ct),

                NoticeKind.S52 =>
                    await FetchS52EmailAsync(
                        settings.RollId,
                        log.AppealNo
                            ?? "",
                        settings.IsSection52Review == true,
                        string.IsNullOrWhiteSpace(
                            log.RecipientName)
                            ? null
                            : log.RecipientName.Trim(),
                        ct),

                NoticeKind.S53 or NoticeKind.S53Rev =>
                    (await FetchS53MvdEmailDataAsync(
                        settings.Notice,
                        settings.RollId,
                        log.ObjectionNo
                            ?? "",
                        batchName,
                        string.IsNullOrWhiteSpace(
                            log.RecipientName)
                            ? "Owner"
                            : log.RecipientName.Trim(),
                        ct))?.Email,

                _ =>
                    null
            };
        }

        private async Task<string?> FetchS49EmailAsync(
            int rollId,
            string? premiseId,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(
                    premiseId))
            {
                return null;
            }

            try
            {
                var (_, contact) =
                    await _s49Repo.LoadPremiseAsync(
                        rollId,
                        premiseId,
                        ct);

                return string.IsNullOrWhiteSpace(
                    contact?.Email)
                    ? null
                    : contact.Email.Trim();
            }
            catch (Exception ex)
            {
                _log.LogWarning(
                    ex,
                    "FetchS49EmailAsync failed for PremiseId={PremiseId}; cached email will be used.",
                    premiseId);

                return null;
            }
        }

        private async Task<string?> FetchS51EmailAsync(
            int rollId,
            string? objectionNo,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(
                    objectionNo))
            {
                return null;
            }

            try
            {
                var cs =
                    _config.GetConnectionString(
                        "DefaultConnection")
                    ?? throw new InvalidOperationException(
                        "DefaultConnection is not configured.");

                await using var cn =
                    new SqlConnection(
                        cs);

                await using var cmd =
                    new SqlCommand(
                        "dbo.S51_GetNoticeRow",
                        cn)
                    {
                        CommandType =
                            CommandType.StoredProcedure,

                        CommandTimeout =
                            30
                    };

                cmd.Parameters.AddWithValue(
                    "@RollId",
                    rollId);

                cmd.Parameters.AddWithValue(
                    "@ObjectionNo",
                    objectionNo);

                await cn.OpenAsync(ct);

                await using var reader =
                    await cmd.ExecuteReaderAsync(
                        ct);

                if (!await reader.ReadAsync(ct))
                    return null;

                var columns =
                    BuildColumnMap(
                        reader);

                return columns.TryGetValue(
                           "RecipientEmail",
                           out var ordinal) &&
                       !reader.IsDBNull(
                           ordinal)
                    ? reader.GetValue(
                            ordinal)
                        ?.ToString()
                        ?.Trim()
                    : null;
            }
            catch (Exception ex)
            {
                _log.LogWarning(
                    ex,
                    "FetchS51EmailAsync failed for ObjectionNo={ObjectionNo}; cached email will be used.",
                    objectionNo);

                return null;
            }
        }

        private async Task<string?> FetchS52EmailAsync(
            int rollId,
            string appealNo,
            bool isReview,
            string? appealType,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(
                    appealNo))
            {
                return null;
            }

            try
            {
                var cs =
                    _config.GetConnectionString(
                        "DefaultConnection")
                    ?? throw new InvalidOperationException(
                        "DefaultConnection is not configured.");

                var proc =
                    isReview
                        ? "dbo.S52_Preview_SelectReviewTop1"
                        : "dbo.S52_Preview_SelectAppealTop1";

                await using var cn =
                    new SqlConnection(
                        cs);

                await using var cmd =
                    cn.CreateCommand();

                cmd.CommandText =
                    proc;

                cmd.CommandType =
                    CommandType.StoredProcedure;

                cmd.CommandTimeout =
                    30;

                cmd.Parameters.Add(
                    new SqlParameter(
                        "@RollId",
                        SqlDbType.Int)
                    {
                        Value =
                            rollId
                    });

                cmd.Parameters.Add(
                    new SqlParameter(
                        "@AppealNo",
                        SqlDbType.VarChar,
                        50)
                    {
                        Value =
                            appealNo.Trim()
                    });

                if (!string.IsNullOrWhiteSpace(
                        appealType))
                {
                    cmd.Parameters.Add(
                        new SqlParameter(
                            "@AppealType",
                            SqlDbType.NVarChar,
                            50)
                        {
                            Value =
                                appealType.Trim()
                        });
                }

                await cn.OpenAsync(ct);

                await using var reader =
                    await cmd.ExecuteReaderAsync(
                        ct);

                if (!await reader.ReadAsync(ct))
                    return null;

                var columns =
                    BuildColumnMap(
                        reader);

                return columns.TryGetValue(
                           "Email",
                           out var ordinal) &&
                       !reader.IsDBNull(
                           ordinal)
                    ? reader.GetValue(
                            ordinal)
                        ?.ToString()
                        ?.Trim()
                    : null;
            }
            catch (Exception ex)
            {
                _log.LogWarning(
                    ex,
                    "FetchS52EmailAsync failed for AppealNo={AppealNo}; cached email will be used.",
                    appealNo);

                return null;
            }
        }

        private async Task<S53MvdEmailData?> FetchS53MvdEmailDataAsync(
            NoticeKind notice,
            int rollId,
            string objectionNo,
            string batchName,
            string objectorType,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(
                    objectionNo))
            {
                return null;
            }

            var cs =
                _config.GetConnectionString(
                    "DefaultConnection")
                ?? throw new InvalidOperationException(
                    "DefaultConnection is not configured.");

            var proc =
                notice == NoticeKind.S53Rev
                    ? "dbo.S53Rev_GetMvdRow"
                    : "dbo.S53_GetMvdRow";

            await using var cn =
                new SqlConnection(
                    cs);

            await using var cmd =
                new SqlCommand(
                    proc,
                    cn)
                {
                    CommandType =
                        CommandType.StoredProcedure,

                    CommandTimeout =
                        60
                };

            cmd.Parameters.AddWithValue(
                "@RollId",
                rollId);

            cmd.Parameters.AddWithValue(
                "@ObjectionNo",
                objectionNo);

            cmd.Parameters.AddWithValue(
                "@BatchName",
                batchName);

            cmd.Parameters.AddWithValue(
                "@ObjectorType",
                objectorType);

            await cn.OpenAsync(ct);

            await using var reader =
                await cmd.ExecuteReaderAsync(
                    ct);

            if (!await reader.ReadAsync(ct))
                return null;

            var columns =
                BuildColumnMap(
                    reader);

            string? Read(
                string name)
            {
                return columns.TryGetValue(
                           name,
                           out var ordinal) &&
                       !reader.IsDBNull(
                           ordinal)
                    ? reader.GetValue(
                            ordinal)
                        ?.ToString()
                        ?.Trim()
                    : null;
            }

            return new S53MvdEmailData
            {
                ObjectionNo =
                    Read(
                        "Objection_No")
                    ?? objectionNo,

                PropertyDesc =
                    Read(
                        "PropertyDesc"),

                Email =
                    Read(
                        "Email")
            };
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private static TimeZoneInfo GetSouthAfricaTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(
                    "South Africa Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.FindSystemTimeZoneById(
                    "Africa/Johannesburg");
            }
        }

        private static DateTimeOffset SouthAfricaNow()
        {
            return TimeZoneInfo.ConvertTime(
                DateTimeOffset.UtcNow,
                SouthAfricaTimeZone);
        }

        private static string BuildTrackingReference(
            NoticeSettings settings,
            NoticeRunLog log)
        {
            return log.ObjectionNo
                   ?? log.AppealNo
                   ?? log.PremiseId
                   ?? $"{settings.Notice}-{log.Id}";
        }

        private static Dictionary<string, int> BuildColumnMap(
            SqlDataReader reader)
        {
            var map =
                new Dictionary<string, int>(
                    StringComparer.OrdinalIgnoreCase);

            for (var i = 0;
                 i < reader.FieldCount;
                 i++)
            {
                map[reader.GetName(i)] =
                    i;
            }

            return map;
        }

        private static object DbValue(
            string? value)
        {
            return string.IsNullOrWhiteSpace(
                    value)
                ? DBNull.Value
                : value;
        }

        private static string QuoteSqlIdentifier(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                throw new InvalidOperationException(
                    "SQL identifier cannot be empty.");
            }

            return
                $"[{value.Replace("]", "]]")}]";
        }

        private static string LimitError(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return "Unknown email error.";
            }

            return value.Length > 2000
                ? value[..2000]
                : value;
        }

        private static string MakeSafeFilePart(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return "";
            }

            var clean =
                value.Trim();

            foreach (var character in
                     Path.GetInvalidFileNameChars())
            {
                clean =
                    clean.Replace(
                        character,
                        '_');
            }

            clean =
                clean
                    .Replace(" ", "_")
                    .Replace("/", "_")
                    .Replace("\\", "_")
                    .Replace(":", "_")
                    .Replace("*", "_")
                    .Replace("?", "_")
                    .Replace("\"", "_")
                    .Replace("<", "_")
                    .Replace(">", "_")
                    .Replace("|", "_");

            while (clean.Contains("__"))
            {
                clean =
                    clean.Replace(
                        "__",
                        "_");
            }

            return clean.Trim('_');
        }

        private static string MakeSafeFilePartWithExtension(
            string value,
            string extension)
        {
            var desiredExtension =
                extension.StartsWith(".")
                    ? extension
                    : "." + extension;

            var name =
                Path.GetFileNameWithoutExtension(
                    value);

            name =
                MakeSafeFilePart(
                    name);

            if (string.IsNullOrWhiteSpace(
                    name))
            {
                name =
                    "email";
            }

            return
                name + desiredExtension;
        }

        private static void WriteBase64Lines(
            StringBuilder sb,
            string base64)
        {
            for (var i = 0;
                 i < base64.Length;
                 i += 76)
            {
                sb.AppendLine(
                    base64.Substring(
                        i,
                        Math.Min(
                            76,
                            base64.Length - i)));
            }
        }

        private static string EncodeMailHeader(
            string value)
        {
            return value
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        }

        private sealed class S53MvdEmailData
        {
            public string? ObjectionNo { get; set; }

            public string? PropertyDesc { get; set; }

            public string? Email { get; set; }
        }
    }
}
