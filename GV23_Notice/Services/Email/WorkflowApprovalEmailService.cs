using GV23_Notice.Domain.Email;
using GV23_Notice.Domain.Rolls;
using GV23_Notice.Domain.Workflow.Entities;
using Microsoft.Extensions.Options;
using System.Text;

namespace GV23_Notice.Services.Email
{
    public sealed class WorkflowApprovalEmailService : IWorkflowApprovalEmailService
    {
        private readonly EmailTemplateOptions _opt;

        public WorkflowApprovalEmailService(IOptions<EmailTemplateOptions> opt)
        {
            _opt = opt.Value;
        }

        // ─────────────────────────────────────────────────────────────────────
        // APPROVAL EMAIL  (Step 2 → Step 3 handover)
        // ─────────────────────────────────────────────────────────────────────
        public (string Subject, string BodyHtml) BuildApprovalEmail(
            NoticeSettings s,
            RollRegistry roll,
            string approvedBy,
            Guid workflowKey,
            string kickoffBaseUrl,
            string? appealKickoffUrl = null,
            string? reviewKickoffUrl = null,
            string? splitPdfKickoffUrl = null)
        {
            var join = kickoffBaseUrl.Contains('?') ? "&" : "?";
            var kickoffUrl = $"{kickoffBaseUrl}{join}key={workflowKey:D}";
            var subject = $"[APPROVED] {roll.ShortCode} {s.Notice} (v{s.Version}) – Step 3 Kickoff Ready";

            var noticeRows = BuildNoticeDetailRows(s);

            var body = $@"
{Header()}
<table role='presentation' width='100%' cellpadding='0' cellspacing='0' style='max-width:640px;margin:0 auto;background:#ffffff;border-radius:0 0 8px 8px;overflow:hidden;border:1px solid #e0e0e0;border-top:none;'>


  <tr>
    <td style='background:#e6b000;padding:24px 32px;text-align:center;'>
      <div style='display:inline-block;background:#ffffff;border-radius:50%;width:56px;height:56px;line-height:56px;font-size:28px;margin-bottom:12px;'>✅</div>
      <h1 style='margin:0;font-size:22px;font-weight:700;color:#111111;letter-spacing:0.5px;'>Step 2 Approved</h1>
      <p style='margin:6px 0 0;font-size:13px;color:#333333;'>The workflow is ready to proceed to <strong>Step 3 – Data Team Processing</strong></p>
    </td>
  </tr>


  <tr>
    <td style='padding:28px 32px;'>

      <p style='margin:0 0 20px;font-size:14px;color:#222222;line-height:1.6;'>
        Good day Team,<br/><br/>
        The Step 2 preview and approval has been <strong style='color:#b38900;'>completed and approved</strong>.
        The Data Team may now proceed to Step 3 using the secure kickoff link below.
      </p>

      {BuildKickoffLinksBlock(s, kickoffUrl, appealKickoffUrl, reviewKickoffUrl, workflowKey)}

   
      <p style='margin:0 0 10px;font-size:13px;font-weight:700;color:#111111;text-transform:uppercase;letter-spacing:0.6px;border-bottom:2px solid #e6b000;padding-bottom:6px;'>Workflow Summary</p>
      <table role='presentation' width='100%' cellpadding='0' cellspacing='0' style='border-collapse:collapse;margin-bottom:24px;font-size:13px;'>
        {SummaryRow("Roll", $"{H(roll.ShortCode)} – {H(roll.Name)}")}
        {SummaryRow("Notice Type", H(s.Notice.ToString()))}
        {SummaryRow("Batch Mode", H(s.Mode.ToString()))}
        {SummaryRow("Version", $"v{s.Version}")}
        {SummaryRow("Approved By", H(approvedBy))}
        {SummaryRow("Letter Date", s.LetterDate.ToString("dd MMMM yyyy"))}
      </table>

      {(noticeRows.Length > 0 ? $@"
      <p style='margin:0 0 10px;font-size:13px;font-weight:700;color:#111111;text-transform:uppercase;letter-spacing:0.6px;border-bottom:2px solid #e6b000;padding-bottom:6px;'>Step 1 Configuration</p>
      <table role='presentation' width='100%' cellpadding='0' cellspacing='0' style='border-collapse:collapse;margin-bottom:24px;font-size:13px;'>
        {noticeRows}
      </table>" : "")}

      <p style='margin:0 0 6px;font-size:12px;color:#888888;font-style:italic;'>
        Note: This email is a workflow handover notification. Client-facing emails are dispatched from Step 3 only.
      </p>

    </td>
  </tr>

  {Footer()}
</table>
{Wrapper_End()}";

            return (subject, body);
        }

        // ─────────────────────────────────────────────────────────────────────
        // CORRECTION REQUEST EMAIL  (Step 2 → admin back to Step 1)
        // ─────────────────────────────────────────────────────────────────────
        public (string Subject, string BodyHtml) BuildCorrectionEmail(
            NoticeSettings s,
            RollRegistry roll,
            string requestedBy,
            string reason,
            Guid workflowKey,
            string step2Url)
        {
            var subject = $"[CORRECTION REQUIRED] {roll.ShortCode} {s.Notice} (v{s.Version}) – Action Needed";

            var body = $@"
{Header()}
<table role='presentation' width='100%' cellpadding='0' cellspacing='0' style='max-width:640px;margin:0 auto;background:#ffffff;border-radius:0 0 8px 8px;overflow:hidden;border:1px solid #e0e0e0;border-top:none;'>


  <tr>
    <td style='background:#e6b000;padding:24px 32px;text-align:center;'>
      <div style='display:inline-block;background:#ffffff;border-radius:50%;width:56px;height:56px;line-height:56px;font-size:28px;margin-bottom:12px;'>🔁</div>
      <h1 style='margin:0;font-size:22px;font-weight:700;color:#111111;letter-spacing:0.5px;'>Correction Requested</h1>
      <p style='margin:6px 0 0;font-size:13px;color:#333333;'>The configuration must be updated before the workflow can proceed to Step 3</p>
    </td>
  </tr>

 
  <tr>
    <td style='padding:28px 32px;'>

      <p style='margin:0 0 20px;font-size:14px;color:#222222;line-height:1.6;'>
        Good day Team,<br/><br/>
        A <strong style='color:#b38900;'>correction has been requested</strong> during <strong>Step 2 (Preview &amp; Approval)</strong>.
        Please review the reason below, update the configuration in Step 1, and re-submit for approval.
      </p>

      <p style='margin:0 0 10px;font-size:13px;font-weight:700;color:#111111;text-transform:uppercase;letter-spacing:0.6px;border-bottom:2px solid #e6b000;padding-bottom:6px;'>Reason / Required Changes</p>
      <table role='presentation' width='100%' cellpadding='0' cellspacing='0' style='margin-bottom:24px;'>
        <tr>
          <td style='background:#fdf8e8;border:1px solid #e6b000;border-left:4px solid #e6b000;border-radius:6px;padding:16px 18px;font-size:13px;color:#222222;line-height:1.7;'>
            {H(reason).Replace("\n", "<br/>")}
          </td>
        </tr>
      </table>

  
      <p style='margin:0 0 10px;font-size:13px;font-weight:700;color:#111111;text-transform:uppercase;letter-spacing:0.6px;border-bottom:2px solid #e6b000;padding-bottom:6px;'>Correction Details</p>
      <table role='presentation' width='100%' cellpadding='0' cellspacing='0' style='border-collapse:collapse;margin-bottom:24px;font-size:13px;'>
        {SummaryRow("Requested By", H(requestedBy))}
        {SummaryRow("Requested At", DateTime.Now.ToString("dd MMMM yyyy HH:mm"))}
        {SummaryRow("Workflow Key", H(workflowKey.ToString("D")))}
      </table>

    
      <p style='margin:0 0 10px;font-size:13px;font-weight:700;color:#111111;text-transform:uppercase;letter-spacing:0.6px;border-bottom:2px solid #e6b000;padding-bottom:6px;'>Workflow Context</p>
      <table role='presentation' width='100%' cellpadding='0' cellspacing='0' style='border-collapse:collapse;margin-bottom:24px;font-size:13px;'>
        {SummaryRow("Roll", $"{H(roll.ShortCode)} – {H(roll.Name)}")}
        {SummaryRow("Notice Type", H(s.Notice.ToString()))}
        {SummaryRow("Batch Mode", H(s.Mode.ToString()))}
        {SummaryRow("Version", $"v{s.Version}")}
        {SummaryRow("Letter Date", s.LetterDate.ToString("dd MMMM yyyy"))}
      </table>

 
      {(!string.IsNullOrWhiteSpace(step2Url) ? $@"
      <table role='presentation' cellpadding='0' cellspacing='0' width='100%' style='margin:0 0 20px;'>
        <tr>
          <td style='background:#fdf8e8;border:1px solid #e6b000;border-left:4px solid #e6b000;border-radius:6px;padding:18px 20px;'>
            <p style='margin:0 0 6px;font-size:12px;font-weight:700;color:#b38900;text-transform:uppercase;letter-spacing:0.8px;'>Return to Step 2 Review</p>
            <a href='{H(step2Url)}' style='font-size:13px;color:#0066cc;word-break:break-all;text-decoration:underline;'>{H(step2Url)}</a>
          </td>
        </tr>
      </table>" : "")}

      <p style='margin:0;font-size:13px;color:#555555;line-height:1.6;'>
        Please update the configuration and re-submit for approval once the corrections have been made.
      </p>

    </td>
  </tr>

  {Footer()}
</table>
{Wrapper_End()}";

            return (subject, body);
        }

        // ─────────────────────────────────────────────────────────────────────
        // PRIVATE HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private string BuildNoticeDetailRows(NoticeSettings s)
        {
            var sb = new StringBuilder();

            // Common fields
            if (!string.IsNullOrWhiteSpace(s.PortalUrl))
                sb.Append(SummaryRow("Portal URL", H(s.PortalUrl)));
            if (!string.IsNullOrWhiteSpace(s.EnquiriesLine))
                sb.Append(SummaryRow("Enquiries Line", H(s.EnquiriesLine)));
            if (s.CityManagerSignDate.HasValue)
                sb.Append(SummaryRow("City Manager Sign Date", s.CityManagerSignDate.Value.ToString("dd MMMM yyyy")));

            switch (s.Notice)
            {
                case Domain.Workflow.NoticeKind.S49:
                    if (s.ObjectionStartDate.HasValue)
                        sb.Append(SummaryRow("Objection Start Date", s.ObjectionStartDate.Value.ToString("dd MMMM yyyy")));
                    if (s.ObjectionEndDate.HasValue)
                        sb.Append(SummaryRow("Objection End Date", s.ObjectionEndDate.Value.ToString("dd MMMM yyyy")));
                    if (s.ExtensionDate.HasValue)
                        sb.Append(SummaryRow("Extension Date", s.ExtensionDate.Value.ToString("dd MMMM yyyy")));
                    sb.Append(SummaryRow("Signature Uploaded", string.IsNullOrWhiteSpace(s.SignaturePath) ? "No" : "Yes"));
                    break;

                case Domain.Workflow.NoticeKind.S51:
                    if (s.EvidenceCloseDate.HasValue)
                        sb.Append(SummaryRow("Evidence Close Date", s.EvidenceCloseDate.Value.ToString("dd MMMM yyyy")));
                    break;

                case Domain.Workflow.NoticeKind.S52:
                    // Which sub-type(s) will be sent to Data Team
                    var modeLabel = s.S52SendMode switch
                    {
                        Domain.Workflow.S52SendMode.ReviewOnly => "Section 52 Review Notice only",
                        Domain.Workflow.S52SendMode.AppealDecisionOnly => "Appeal Decision Notice only",
                        Domain.Workflow.S52SendMode.Both => "Both — Section 52 Review Notice AND Appeal Decision Notice",
                        _ => "Unknown"
                    };
                    sb.Append(SummaryRow("Sub-Type to Send", $"<strong>{modeLabel}</strong>"));
                    if (s.BulkFromDate.HasValue)
                        sb.Append(SummaryRow("Batch Date From", s.BulkFromDate.Value.ToString("dd MMMM yyyy")));
                    if (s.BulkToDate.HasValue)
                        sb.Append(SummaryRow("Batch Date To", s.BulkToDate.Value.ToString("dd MMMM yyyy")));
                    break;

                case Domain.Workflow.NoticeKind.S53:
                    if (s.BatchDate.HasValue)
                        sb.Append(SummaryRow("Batch Date", s.BatchDate.Value.ToString("dd MMMM yyyy")));
                    if (s.AppealCloseDate.HasValue)
                        sb.Append(SummaryRow("Appeal Close Date", s.AppealCloseDate.Value.ToString("dd MMMM yyyy")));
                    if (!string.IsNullOrWhiteSpace(s.AppealCloseOverrideReason))
                    {
                        sb.Append(SummaryRow("Appeal Close Overridden", "Yes"));
                        sb.Append(SummaryRow("Override Reason", H(s.AppealCloseOverrideReason)));
                    }
                    break;

                case Domain.Workflow.NoticeKind.S78:
                    if (s.ExtractionDate.HasValue)
                        sb.Append(SummaryRow("Extraction Date", s.ExtractionDate.Value.ToString("dd MMMM yyyy")));
                    if (s.ExtractPeriodDays.HasValue)
                        sb.Append(SummaryRow("Extract Period (Days)", s.ExtractPeriodDays.Value.ToString()));
                    if (s.ReviewOpenDate.HasValue)
                        sb.Append(SummaryRow("Review Open Date", s.ReviewOpenDate.Value.ToString("dd MMMM yyyy")));
                    if (s.ReviewCloseDate.HasValue)
                        sb.Append(SummaryRow("Review Close Date", s.ReviewCloseDate.Value.ToString("dd MMMM yyyy")));
                    break;
            }

            return sb.ToString();
        }

        /// <summary>Alternating-row table row with gold label column.</summary>
        // ── Kickoff link block — single for all notices, dual for S52 ────────
        private static string BuildKickoffLinksBlock(
            NoticeSettings s,
            string kickoffUrl,
            string? appealKickoffUrl,
            string? reviewKickoffUrl,
            Guid workflowKey,
            string? splitPdfKickoffUrl = null)
        {
            var key = H(workflowKey.ToString("D"));

            // S52 with one or both separate URLs provided
            if (s.Notice == Domain.Workflow.NoticeKind.S52 &&
                (appealKickoffUrl != null || reviewKickoffUrl != null))
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("<table role='presentation' cellpadding='0' cellspacing='0' width='100%' style='margin:0 0 28px;'>");

                if (appealKickoffUrl != null &&
                    s.S52SendMode != Domain.Workflow.S52SendMode.ReviewOnly)
                {
                    sb.Append($@"
        <tr>
          <td style='background:#fff4e0;border:1px solid #e6a800;border-left:4px solid #e6a800;border-radius:6px;padding:18px 20px;margin-bottom:12px;'>
            <p style='margin:0 0 4px;font-size:12px;font-weight:700;color:#b38900;text-transform:uppercase;letter-spacing:0.8px;'>Appeal Decision — Step 3 Kickoff (Secure)</p>
            <a href='{H(appealKickoffUrl)}' style='font-size:13px;color:#0066cc;word-break:break-all;text-decoration:underline;'>{H(appealKickoffUrl)}</a>
            <p style='margin:10px 0 0;font-size:11px;color:#888888;'>Workflow Key: {key}</p>
          </td>
        </tr>
        <tr><td style='height:10px;'></td></tr>");
                }

                if (reviewKickoffUrl != null &&
                    s.S52SendMode != Domain.Workflow.S52SendMode.AppealDecisionOnly)
                {
                    sb.Append($@"
        <tr>
          <td style='background:#f0f4ff;border:1px solid #4a6cf7;border-left:4px solid #4a6cf7;border-radius:6px;padding:18px 20px;'>
            <p style='margin:0 0 4px;font-size:12px;font-weight:700;color:#3355cc;text-transform:uppercase;letter-spacing:0.8px;'>Section 52 Review — Step 3 Kickoff (Secure)</p>
            <a href='{H(reviewKickoffUrl)}' style='font-size:13px;color:#0066cc;word-break:break-all;text-decoration:underline;'>{H(reviewKickoffUrl)}</a>
            <p style='margin:10px 0 0;font-size:11px;color:#888888;'>Workflow Key: {key}</p>
          </td>
        </tr>");
                }

                sb.Append("</table>");
                return sb.ToString();
            }

            // Standard kickoff links: single + split PDF (when applicable)
            var sb2 = new System.Text.StringBuilder();
            sb2.Append("<table role='presentation' cellpadding='0' cellspacing='0' width='100%' style='margin:0 0 28px;'>");

            // Single PDF link
            sb2.Append($@"
        <tr>
          <td style='background:#fdf8e8;border:1px solid #e6b000;border-left:4px solid #e6b000;border-radius:6px;padding:18px 20px;'>
            <p style='margin:0 0 4px;font-size:12px;font-weight:700;color:#b38900;text-transform:uppercase;letter-spacing:0.8px;'>Single PDF — Step 3 Kickoff (Secure)</p>
            <a href='{H(kickoffUrl)}' style='font-size:13px;color:#0066cc;word-break:break-all;text-decoration:underline;'>{H(kickoffUrl)}</a>
            <p style='margin:10px 0 0;font-size:11px;color:#888888;'>Workflow Key: {key}</p>
          </td>
        </tr>");

            // Split PDF link (when notice supports it)
            if (!string.IsNullOrWhiteSpace(splitPdfKickoffUrl))
            {
                sb2.Append($@"
        <tr><td style='height:10px;'></td></tr>
        <tr>
          <td style='background:#f0f9f4;border:1px solid #2a8c4a;border-left:4px solid #2a8c4a;border-radius:6px;padding:18px 20px;'>
            <p style='margin:0 0 4px;font-size:12px;font-weight:700;color:#1b5e20;text-transform:uppercase;letter-spacing:0.8px;'>Split / Multipurpose PDF — Step 3 Kickoff (Secure)</p>
            <a href='{H(splitPdfKickoffUrl)}' style='font-size:13px;color:#0066cc;word-break:break-all;text-decoration:underline;'>{H(splitPdfKickoffUrl)}</a>
            <p style='margin:10px 0 0;font-size:11px;color:#888888;'>Use this link for split/multipurpose properties. Workflow Key: {key}</p>
          </td>
        </tr>");
            }

            sb2.Append("</table>");
            return sb2.ToString();
        }

        private static string SummaryRow(string label, string value, bool alt = false)
        {
            var bg = alt ? "#fafafa" : "#ffffff";
            return $@"
<tr style='background:{bg};'>
  <td style='border:1px solid #e8e8e8;padding:9px 12px;width:38%;font-weight:600;color:#555555;white-space:nowrap;'>{label}</td>
  <td style='border:1px solid #e8e8e8;padding:9px 12px;color:#111111;'>{value}</td>
</tr>";
        }

        private static string Header() => @"
<div style='background:#f5f5f5;padding:24px 16px 0;font-family:Arial,Helvetica,sans-serif;'>
<table role='presentation' width='100%' cellpadding='0' cellspacing='0' style='max-width:640px;margin:0 auto;'>
  <tr>
    <td style='background:#e6b000;padding:18px 32px;border-radius:8px 8px 0 0;'>
      <table role='presentation' width='100%' cellpadding='0' cellspacing='0'>
        <tr>
          <td>
            <p style='margin:0;font-size:11px;font-weight:700;color:#111111;text-transform:uppercase;letter-spacing:1px;'>City of Johannesburg</p>
            <p style='margin:2px 0 0;font-size:10px;color:#333333;letter-spacing:0.3px;'>Group Finance: Property Branch · Valuation Services</p>
          </td>
          <td style='text-align:right;'>
            <p style='margin:0;font-size:10px;color:#333333;'>GV23 Notices System</p>
          </td>
        </tr>
      </table>
    </td>
  </tr>
</table>";

        private static string Footer() => $@"
  <tr>
    <td style='background:#f9f9f9;border-top:3px solid #e6b000;padding:20px 32px;'>
      <p style='margin:0 0 4px;font-size:13px;color:#333333;font-weight:600;'>City of Johannesburg</p>
      <p style='margin:0 0 4px;font-size:12px;color:#666666;'>Valuation Services Department</p>
      <p style='margin:0;font-size:11px;color:#999999;'>This is an automated workflow notification from the GV23 Notices System. Please do not reply to this email.</p>
    </td>
  </tr>";

        private static string Wrapper_End() => "</div>";

        private static string H(string? s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
    }
}