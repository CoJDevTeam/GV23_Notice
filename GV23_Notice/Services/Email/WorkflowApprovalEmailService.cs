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

        public (string Subject, string BodyHtml) BuildApprovalEmail(
     NoticeSettings s,
     RollRegistry roll,
     string approvedBy,
     Guid workflowKey,
     string kickoffBaseUrl)
        {
            // Kickoff link built from key
            // kickoffBaseUrl example: https://yourdomain/Step3/Kickoff
            var kickoffUrl = $"{kickoffBaseUrl}?key={workflowKey:D}";

            var subject = $"[APPROVED] {roll.ShortCode} {s.Notice} (v{s.Version}) - Step 3 Kickoff";

            var sb = new StringBuilder();
            sb.Append("<div style='font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:1.5;color:#111'>");

            sb.Append("<p>Good day Team,</p>");
            sb.Append("<p>");
            sb.Append("Step 2 has been <b>APPROVED</b> and the workflow may proceed to <b>Step 3</b> (Data Team processing).");
            sb.Append("</p>");

            sb.Append("<p><b>Kickoff Link (secure):</b><br/>");
            sb.Append($"<a href='{Html(kickoffUrl)}'>{Html(kickoffUrl)}</a></p>");

            // ✅ Include key as reference
            sb.Append("<p><b>Workflow Key:</b> ");
            sb.Append(Html(workflowKey.ToString("D")));
            sb.Append("</p>");

            sb.Append("<hr style='border:0;border-top:1px solid #ddd;margin:14px 0'/>");

            sb.Append("<p><b>Workflow Summary</b></p>");
            sb.Append("<ul>");
            sb.Append($"<li><b>Roll:</b> {Html(roll.ShortCode)} - {Html(roll.Name)}</li>");
            sb.Append($"<li><b>Notice:</b> {Html(s.Notice.ToString())}</li>");
            sb.Append($"<li><b>Mode:</b> {Html(s.Mode.ToString())}</li>");
            sb.Append($"<li><b>Version:</b> v{s.Version}</li>");
            sb.Append($"<li><b>Approved By:</b> {Html(approvedBy)}</li>");
            sb.Append($"<li><b>Letter Date:</b> {s.LetterDate:yyyy-MM-dd}</li>");
            sb.Append("</ul>");

            sb.Append("<p><b>Step 1 Details</b></p>");
            sb.Append("<table style='border-collapse:collapse;width:100%'>");
            Row(sb, "Portal URL", s.PortalUrl);
            Row(sb, "Enquiries Line", s.EnquiriesLine);
            Row(sb, "City Manager Sign Date", s.CityManagerSignDate?.ToString("yyyy-MM-dd"));

            if (s.Notice == Domain.Workflow.NoticeKind.S49)
            {
                Row(sb, "Objection Start Date", s.ObjectionStartDate?.ToString("yyyy-MM-dd"));
                Row(sb, "Objection End Date", s.ObjectionEndDate?.ToString("yyyy-MM-dd"));
                Row(sb, "Extension Date", s.ExtensionDate?.ToString("yyyy-MM-dd"));
                Row(sb, "Signature Uploaded", string.IsNullOrWhiteSpace(s.SignaturePath) ? "NO" : "YES");
                Row(sb, "Signature Path", s.SignaturePath);
            }

            if (s.Notice == Domain.Workflow.NoticeKind.S51)
                Row(sb, "Evidence Close Date", s.EvidenceCloseDate?.ToString("yyyy-MM-dd"));

            if (s.Notice == Domain.Workflow.NoticeKind.S52)
            {
                Row(sb, "Bulk From", s.BulkFromDate?.ToString("yyyy-MM-dd"));
                Row(sb, "Bulk To", s.BulkToDate?.ToString("yyyy-MM-dd"));
            }

            if (s.Notice == Domain.Workflow.NoticeKind.S53)
            {
                Row(sb, "Batch Date", s.BatchDate?.ToString("yyyy-MM-dd"));
                Row(sb, "Appeal Close Date", s.AppealCloseDate?.ToString("yyyy-MM-dd"));
                Row(sb, "Appeal Close Override", string.IsNullOrWhiteSpace(s.AppealCloseOverrideReason) ? "NO" : "YES");
                Row(sb, "Override Reason", s.AppealCloseOverrideReason);
                Row(sb, "Override Evidence Path", s.AppealCloseOverrideEvidencePath);
            }

            if (s.Notice == Domain.Workflow.NoticeKind.S78)
            {
                Row(sb, "Extraction Date", s.ExtractionDate?.ToString("yyyy-MM-dd"));
                Row(sb, "Extract Period Days", s.ExtractPeriodDays?.ToString());
                Row(sb, "Review Open Date", s.ReviewOpenDate?.ToString("yyyy-MM-dd"));
                Row(sb, "Review Close Date", s.ReviewCloseDate?.ToString("yyyy-MM-dd"));
            }

            sb.Append("</table>");

            sb.Append("<p style='margin-top:14px'><b>Note:</b> This email is generated for workflow handover. Client emails are sent from Step 3 only.</p>");
            sb.Append("<p>Regards,<br/>City of Johannesburg<br/>Valuation Services</p>");

            sb.Append("<p style='font-size:11px;color:#666'>This is an automated workflow notification.</p>");
            sb.Append("</div>");

            return (subject, sb.ToString());
        }

        public (string Subject, string BodyHtml) BuildCorrectionEmail(
      NoticeSettings s,
      RollRegistry roll,
      string requestedBy,
      string reason,
      Guid workflowKey)
        {
            var subject = $"[CORRECTION REQUEST] {roll.ShortCode} {s.Notice} (v{s.Version}) - Step 1 Settings";

            var sb = new StringBuilder();
            sb.Append("<div style='font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:1.5;color:#111'>");
            sb.Append("<p>Good day Team,</p>");
            sb.Append("<p>");
            sb.Append("A correction is requested for the <b>Step 1 configuration</b> before Step 3 processing can begin.");
            sb.Append("</p>");

            sb.Append("<p><b>Workflow Key:</b> ");
            sb.Append(Html(workflowKey.ToString("D")));
            sb.Append("</p>");

            sb.Append("<p><b>Requested By:</b> ");
            sb.Append(Html(requestedBy));
            sb.Append("</p>");

            sb.Append("<p><b>Reason / Required Changes:</b><br/>");
            sb.Append(Html(reason).Replace("\n", "<br/>"));
            sb.Append("</p>");

            sb.Append("<hr style='border:0;border-top:1px solid #ddd;margin:14px 0'/>");

            sb.Append("<p><b>Context</b></p>");
            sb.Append("<ul>");
            sb.Append($"<li><b>Roll:</b> {Html(roll.ShortCode)} - {Html(roll.Name)}</li>");
            sb.Append($"<li><b>Notice:</b> {Html(s.Notice.ToString())}</li>");
            sb.Append($"<li><b>Mode:</b> {Html(s.Mode.ToString())}</li>");
            sb.Append($"<li><b>Version:</b> v{s.Version}</li>");
            sb.Append($"<li><b>Letter Date:</b> {s.LetterDate:yyyy-MM-dd}</li>");
            sb.Append("</ul>");

            sb.Append("<p>Regards,<br/>City of Johannesburg<br/>Valuation Services</p>");
            sb.Append("</div>");

            return (subject, sb.ToString());
        }
        private static void Row(StringBuilder sb, string label, string? value)
        {
            value ??= "";
            sb.Append("<tr>");
            sb.Append("<td style='border:1px solid #ddd;padding:8px;width:28%'><b>");
            sb.Append(Html(label));
            sb.Append("</b></td>");
            sb.Append("<td style='border:1px solid #ddd;padding:8px'>");
            sb.Append(Html(value));
            sb.Append("</td>");
            sb.Append("</tr>");
        }

        private static string Html(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
    }
}

