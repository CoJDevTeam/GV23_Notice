using GV23_Notice.Data;
using GV23_Notice.Models.Workflow.ViewModels;
using GV23_Notice.Services.Preview;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services.Step3
{
    public sealed class Step3Step1Service : IStep3Step1Service
    {
        private readonly AppDbContext _db;
        private readonly INoticeAuditLogQueryService _audit;
        private readonly IWorkflowEmailReadService _emailRead;
        private readonly ICorrectionTicketQueryService _tickets;

        public Step3Step1Service(
            AppDbContext db,
            INoticeAuditLogQueryService audit,
            IWorkflowEmailReadService emailRead,
            ICorrectionTicketQueryService tickets)
        {
            _db = db;
            _audit = audit;
            _emailRead = emailRead;
            _tickets = tickets;
        }

        public async Task<Step3Step1Vm> BuildAsync(Guid workflowKey, CancellationToken ct)
        {
            if (workflowKey == Guid.Empty)
                throw new InvalidOperationException("Invalid workflow key.");

            var s = await _db.NoticeSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.WorkflowKey == workflowKey, ct)
                ?? throw new InvalidOperationException("Workflow not found.");

            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.RollId == s.RollId, ct)
                ?? throw new InvalidOperationException("Roll not found.");

            var version = s.Version.ToString();

            // Latest workflow email from runlogs
            WorkflowEmailVm? latestEmail = null;
            var (emlPath, pdfPath, to, createdAtUtc) =
                await _audit.GetLatestStep2AuditEmailAsync(s.RollId, version, workflowKey, ct);

            if (!string.IsNullOrWhiteSpace(emlPath))
            {
                var (subject, body) = _emailRead.TryReadEml(emlPath);
                latestEmail = new WorkflowEmailVm
                {
                    To = to ?? "",
                    Subject = subject,
                    BodyHtml = body,
                    CreatedAtUtc = createdAtUtc,
                    EmlPath = emlPath
                };
            }

            var tickets = await _tickets.ListBySettingsAsync(s.Id, ct);
            var auditLogs = await _audit.GetStep2AuditLogsAsync(s.RollId, version, workflowKey, ct);

            return new Step3Step1Vm
            {
                WorkflowKey = workflowKey,
                SettingsId = s.Id,
                RollId = s.RollId,
                RollShortCode = roll.ShortCode ?? "",
                RollName = roll.Name ?? "",
                Notice = s.Notice,
                Mode = (PreviewMode)s.Mode,
                Version = int.TryParse(version, out var v) ? v : 0,

                LetterDate = s.LetterDate,
                CityManagerSignDate = s.CityManagerSignDate,
                ObjectionStartDate = s.ObjectionStartDate,
                ObjectionEndDate = s.ObjectionEndDate,
                ExtensionDate = s.ExtensionDate,
                EvidenceCloseDate = s.EvidenceCloseDate,
                BulkFromDate = s.BulkFromDate,
                BulkToDate = s.BulkToDate,
                BatchDate = s.BatchDate,
                AppealCloseDate = s.AppealCloseDate,
                PortalUrl = s.PortalUrl,
                EnquiriesLine = s.EnquiriesLine,

                Step1Approved = s.IsApproved,
                Step1ApprovedBy = s.ApprovedBy,           // ✅ adjust to your real field names
                Step1ApprovedAtUtc = s.ApprovedAtUtc,     // ✅ adjust to your real field names

                Step2Approved = s.Step2Approved,
                Step2ApprovedBy = s.Step2ApprovedBy,
                Step2ApprovedAtUtc = s.Step2ApprovedAt,

                Step2CorrectionRequested = s.Step2CorrectionRequested,
                Step2CorrectionRequestedBy = s.Step2CorrectionRequestedBy,
                Step2CorrectionRequestedAtUtc = s.Step2CorrectionRequestedAt,
                Step2CorrectionReason = s.Step2CorrectionReason,

                LatestWorkflowEmail = latestEmail,
                Tickets = tickets,
                AuditLogs = auditLogs
            };
        }
    }
}
