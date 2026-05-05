using GV23_Notice.Domain.Email;
using GV23_Notice.Domain.Workflow;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;

namespace GV23_Notice.Services.Email
{
    public sealed class NoticeEmailTemplateService : INoticeEmailTemplateService
    {
        private readonly EmailTemplateOptions _opt;

        public NoticeEmailTemplateService(IOptions<EmailTemplateOptions> opt)
        {
            _opt = opt.Value;
        }

        public (string Subject, string BodyHtml) Build(NoticeEmailRequest req)
        {
            if (req is null) throw new ArgumentNullException(nameof(req));

            return req.Notice switch
            {
                NoticeKind.S49 => BuildS49(req),
                NoticeKind.S51 => BuildS51(req),
                NoticeKind.S52 => BuildS52(req),
                NoticeKind.S53 => BuildS53(req),
                NoticeKind.DJ => BuildDearJonny(req),
                NoticeKind.IN => BuildInvalid(req),
                NoticeKind.S78 => BuildS78(req),
                _ => (Subject(req, "Notice"), BaseHtml(req, "<p>No email template configured.</p>"))
            };
        }

        // =========================
        // S49
        // =========================
        private (string Subject, string BodyHtml) BuildS49(NoticeEmailRequest req)
        {
            var heading = "PUBLIC NOTICE CALLING FOR INSPECTION OF THE SUPPLEMENTARY VALUATION ROLL AND LODGING OF OBJECTIONS";

            var inspectionStart = req.InspectionStart ?? DateOnly.FromDateTime(DateTime.Today);
            var inspectionEnd = req.InspectionEnd ?? inspectionStart.AddDays(30);

            var dateRangeText = req.ExtendedEnd.HasValue
                ? $"{inspectionStart:dd MMMM yyyy} – {req.ExtendedEnd.Value:dd MMMM yyyy} until 15:00"
                : $"{inspectionStart:dd MMMM yyyy} – {inspectionEnd:dd MMMM yyyy} until 15:00";

            var mid = new StringBuilder();

            mid.Append($"<p><b>{H(heading)}</b></p>");

            // property list
            mid.Append(PropertyBlock(req));

            mid.Append("<p>");
            mid.Append("Notice is hereby given in terms of Section 49(1)(a)(i) read together with section 78 (2) of the ");
            mid.Append("<b>Local Government: Municipal Property Rates Act No. 6 of 2004</b> as amended, ");
            mid.Append("that the ");
            if (!string.IsNullOrWhiteSpace(req.RollTypeText))
                mid.Append($"<b>{H(req.RollTypeText!)}</b> ");
            else
                mid.Append("<b>valuation roll</b> ");

            mid.Append("for the financial years ");
            mid.Append($"<b>{H(req.FinancialYearsText ?? "")}</b> ");
            mid.Append("is open for public inspection ");
            mid.Append($"from <b>{H(dateRangeText)}</b>.");
            mid.Append("</p>");

            return (Subject(req, "Section 49 Notice"), BaseHtml(req, mid.ToString()));
        }

        // =========================
        // S51
        // =========================
        private (string Subject, string BodyHtml) BuildS51(NoticeEmailRequest req)
        {
            var mid = new StringBuilder();

            mid.Append($"<p><b>NOTICE OF {H(req.RollShortCode)} </b><br/><b>Section 51 Notice</b></p>");

            if (req.IsMulti)
            {
                mid.Append("<p>This email serves as a single notice covering <b>multiple properties</b> linked to your email address.<br/>");
                mid.Append("Please review the property descriptions below. A separate PDF notice is attached for each property.</p>");
            }

            mid.Append(PropertyBlock(req));

            if (req.IsMulti)
            {
                mid.Append("<p>You are hereby notified that the Municipal Valuer has received objection(s) from an individual to the above property/ies as reflected in the valuation roll.</p>");
            }
            else
            {
                mid.Append("<p>You are hereby notified that the Municipal Valuer has received an objection from an individual to your property as reflected in the valuation roll.</p>");
            }

            // Submission deadline + portal instructions (mirrors the PDF body)
            var closeDate = req.S51SubmissionsCloseDate?.ToString() ?? "";
            var portal    = req.S51PortalUrl ?? _opt.PortalUrl ?? "https://objections.joburg.org.za";
            var pin       = req.S51Section51Pin ?? "";
            var objNo     = req.S51ObjectionNo ?? req.Items.FirstOrDefault()?.ObjectionNo ?? "";

            if (!string.IsNullOrWhiteSpace(closeDate))
            {
                mid.Append("<p>");
                mid.Append("Submissions by the owner in response to the objections must be submitted online to the Municipal Valuer ");
                mid.Append($"no later than <b>{H(closeDate)}</b> via <a href=\"{H(portal)}\">{H(portal)}</a>. ");
                mid.Append("To attach submissions, click on \u201cUpload Documents,\u201d select \u201cSection 51 Uploads,\u201d ");
                if (!string.IsNullOrWhiteSpace(objNo))
                    mid.Append($"fill in the objection number <b>{H(objNo)}</b> ");
                if (!string.IsNullOrWhiteSpace(pin))
                    mid.Append($"and PIN <b>{H(pin)}</b>, ");
                mid.Append("and then upload the submission documents.");
                mid.Append("</p>");
            }

            mid.Append("<p>");
            mid.Append("You will be notified of the Municipal Valuer\u2019s decision in terms of Section 53 of the ");
            mid.Append("Municipal Property Rates Act 6 of 2004. If you are dissatisfied with the decision, ");
            mid.Append("you will have the right to lodge an appeal.");
            mid.Append("</p>");

            mid.Append(EnquiriesBlock());

            return (Subject(req, "Section 51 Notice"), BaseHtml(req, mid.ToString()));
        }

        // =========================
        // S52 (Review vs Appeal)
        // =========================
        private (string Subject, string BodyHtml) BuildS52(NoticeEmailRequest req)
        {
            var isReview = req.IsSection52Review ?? false;
            var title = isReview ? " SECTION 52 REVIEW " : " APPEAL ";

            var subject =
                $"VALUATION APPEAL BOARD: OUTCOME -{title}DECISIONS FOR THE GENERAL VALUATION ROLL 2023 (GV2023)";

            var greeting = Greeting(req, preferDear: true);

            var mid = new StringBuilder();
            mid.Append($"<p>{greeting}</p>");

            // ✅ NEW: show property + objection/appeal refs
            mid.Append(PropertyBlock(req, includeRefs: true));

            mid.Append("<p>");
            mid.Append("The decision will be adjusted accordingly to the implementation date being, 1 July 2023. The decision ");
            mid.Append("will reflect on your account within 30 days, the adjustments to the account if any will be made by the ");
            mid.Append("Rates and Taxes Department in due course.");
            mid.Append("</p>");

            mid.Append("<p>");
            mid.Append("If you feel aggrieved by the above decision, you are well within your rights to take the matter on ");
            mid.Append("review to the High Court of South Africa at your own cost.");
            mid.Append("</p>");

            mid.Append("<p>Regards,<br/>VALUATION APPEAL BOARD<br/>City of Johannesburg</p>");

            return (subject, WrapHtml(req, mid.ToString()));
        }
        // =========================
        // S53 (your body already strong; we keep it)
        // =========================
        private (string Subject, string BodyHtml) BuildS53(NoticeEmailRequest req)
        {
            var subject = req.Items.Count == 1
                ? $"{req.RollShortCode} Section 53 Objection Decision Notice: {req.Items[0].PropertyDesc}".Trim()
                : $"{req.RollShortCode} Objection Decision Notice ({req.Items.Count} properties)".Trim();

            var mid = new StringBuilder();

            mid.Append("<p>");
            mid.Append(!string.IsNullOrWhiteSpace(req.RecipientName)
                ? $"Good Day {H(req.RecipientName)},"
                : "Good Day,");
            mid.Append("</p>");

            mid.Append("<p>");
            mid.Append("Notice is hereby given in terms of section 53(1) of the Municipal Property Rates Act No.6 of 2004 as amended, ");
            mid.Append("that the objection against the entry of the above property in or omitted from the ");
            mid.Append(H(req.RollName));
            mid.Append(" has been considered by the Municipal Valuer. After reviewing the objection and reasons provided therein, ");
            mid.Append("together with the available market information, attached is the Municipal Valuer&apos;s decision for the property described.");
            mid.Append("</p>");

            mid.Append(PropertyBlock(req, includeRefs: true));

            mid.Append("<p>");
            mid.Append("Should you wish to lodge an appeal against the Municipal Valuer's Decision, please login to the City's online system on the following link: ");
            mid.Append($"<a href=\"{H(_opt.PortalUrl)}\">{H(_opt.PortalUrl)}</a>");
            mid.Append("</p>");

            mid.Append("<p><strong>How to appeal?</strong></p>");
            mid.Append("<ol>");
            mid.Append($"<li>Click the link <a href=\"{H(_opt.PortalUrl)}\">{H(_opt.PortalUrl)}</a></li>");
            mid.Append($"<li>Select <strong>{H(req.RollShortCode)}</strong> roll (on the navigation bar).</li>");
            mid.Append("<li>Select Dashboard.</li>");
            mid.Append("<li>If you are not already logged in, login using the same credentials used for objections.</li>");
            mid.Append("<li>Click the appeal button under the 'Properties objected on' table.</li>");
            mid.Append("<li>Select Property type and Appellant type.</li>");
            mid.Append("<li>Fill in the appeal form and submit.</li>");
            mid.Append("<li>Download the appeal acknowledgement for future references.</li>");
            mid.Append("</ol>");

            mid.Append(EnquiriesBlock());

            mid.Append("<p>Regards,<br/>Municipal Valuer</p>");

            return (subject, WrapHtml(req, mid.ToString()));
        }

        // =========================
        // Dear Jonny
        // =========================
        private (string Subject, string BodyHtml) BuildDearJonny(NoticeEmailRequest req)
        {
            var subject = $"{req.RollName} – RESPONSE OF OBJECTION OUTCOME";

            var mid = new StringBuilder();
            mid.Append($"<p>Dear {H(req.RecipientName)},</p>");
            mid.Append("<p>Please find attached the <strong>Outcome Response</strong> regarding your objection(s) linked to the property/properties listed below.</p>");

            // Table format like your zip
            mid.Append("<table style='border-collapse:collapse; width:100%; margin:12px 0'>");
            mid.Append("<thead><tr>");
            mid.Append("<th style='border:1px solid #ddd; padding:8px; text-align:left'>Objection No</th>");
            mid.Append("<th style='border:1px solid #ddd; padding:8px; text-align:left'>Property Description</th>");
            mid.Append("</tr></thead><tbody>");

            foreach (var i in req.Items)
            {
                mid.Append("<tr>");
                mid.Append($"<td style='border:1px solid #ddd; padding:8px'>{H(i.ObjectionNo ?? "")}</td>");
                mid.Append($"<td style='border:1px solid #ddd; padding:8px'>{H(i.PropertyDesc ?? "")}</td>");
                mid.Append("</tr>");
            }

            mid.Append("</tbody></table>");

            mid.Append("<p><strong>Important:</strong> A Section 53 notice will not be issued as the objection is not valid for the reasons explained in the attached notice.</p>");
            mid.Append("<p>If you wish to proceed, the owner or an authorised representative may submit an application for <strong>condonation for a late appeal</strong>, accompanied by a written motivation.</p>");
            mid.Append("<ul>");
            mid.Append("<li>Late appeal condonation will be considered by the Board and is subject to its discretion.</li>");
            mid.Append("<li>Applications may be submitted via email to <a href='mailto:valuationenquiries@joburg.org.za'>valuationenquiries@joburg.org.za</a> or hand delivered at Jorissen Place, 66 Jorissen Street, 1st Floor, Valuation Administration.</li>");
            mid.Append("</ul>");

            mid.Append("<p>Kind regards,<br/><strong>Valuation Services – City of Johannesburg</strong></p>");
            mid.Append("<p style='font-size:10pt; color:#555'>This email (and any attachments) is intended for the addressee only and may contain confidential information. If you received this in error, please notify the sender and delete it.</p>");

            return (subject, WrapHtml(req, mid.ToString()));
        }

        // =========================
        // Invalid (Omission vs Objection)
        // =========================
        private (string Subject, string BodyHtml) BuildInvalid(NoticeEmailRequest req)
        {
            var kind = req.InvalidKind ?? InvalidNoticeKind.InvalidObjection;

            var kindText = kind == InvalidNoticeKind.InvalidOmission
                ? "Invalid Omission Objection"
                : "Invalid Objection";

            var subject = $"{kindText} - {req.RollShortCode}";

            var reason = kind == InvalidNoticeKind.InvalidOmission
                ? "Please be advised that the objection submitted cannot be considered. The records indicate that the objection was lodged against the incorrect property description."
                : "Please be advised that the objection submitted cannot be considered. The records indicate that the objection was lodged against a property description/property that does not exist on the official applicable property register.";

            var mid = new StringBuilder();
            mid.Append($"<p>Dear <strong>{H(string.IsNullOrWhiteSpace(req.RecipientName) ? "Sir/Madam" : req.RecipientName)}</strong>,</p>");
            mid.Append($"<p>{H(reason)}</p>");
            mid.Append("<p><strong>As a result, a section 53 notice will not be issued, as the objection is not valid for the reasons stated above.</strong></p>");
            mid.Append("<p>Should you wish to submit an objection in line with the applicable property details, please ensure that the correct property description is used.</p>");

            mid.Append(PropertyBlock(req, includeRefs: true));

            mid.Append(EnquiriesBlock());
            mid.Append("<p>Kind regards,<br/>City of Johannesburg Valuation Services</p>");
            mid.Append("<hr style='border:0;border-top:1px solid #ddd;margin:16px 0;'/>");
            mid.Append("<p style='font-size: 11px; color: #666;'>This is an official email generated by the City of Johannesburg Valuation Services Department.</p>");

            return (subject, WrapHtml(req, mid.ToString()));
        }

        // =========================
        // Section 78 (new – professional, aligned to PDF)
        // =========================
        private (string Subject, string BodyHtml) BuildS78(NoticeEmailRequest req)
        {
            // Subject: clear + roll
            var subject = $"{req.RollShortCode} Section 78 Notice - Inspection / Review";

            var mid = new StringBuilder();
            mid.Append($"<p>{Greeting(req, preferDear: true)}</p>");

            if (req.IsMulti)
            {
                mid.Append("<p>This email serves as a single notice covering <b>multiple properties</b> linked to your email address. ");
                mid.Append("Please review the property descriptions below. A separate PDF notice is attached for each property.</p>");
            }

            mid.Append(PropertyBlock(req));

            mid.Append("<p>");
            mid.Append("Please find attached the Section 78 notice for your attention. ");
            mid.Append("Kindly refer to the attached PDF for the full legislative wording, dates and instructions.");
            mid.Append("</p>");

            mid.Append(EnquiriesBlock());
            mid.Append(SignOffBlock());

            return (subject, BaseHtml(req, mid.ToString()));
        }

        // =========================
        // Shared helpers
        // =========================
        private string BaseHtml(NoticeEmailRequest req, string midHtml)
        {
            // Standard wrapper (like your templates)
            return WrapHtml(req, midHtml + SignOffBlock());
        }

        private string WrapHtml(NoticeEmailRequest req, string inner)
        {
            return "<div style='font-family:Arial, Helvetica, sans-serif; font-size:13px; color:#111; line-height:1.5'>" +
                   inner +
                   "</div>";
        }

        private string Greeting(NoticeEmailRequest req, bool preferDear)
        {
            var name = (req.RecipientName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                return preferDear ? "Dear Sir/Madam" : "Good Day,";

            return preferDear ? $"Dear {H(name)}" : $"Good Day {H(name)},";
        }

        private string PropertyBlock(NoticeEmailRequest req, bool includeRefs = false)
        {
            var items = req.Items ?? new List<NoticeEmailPropertyItem>();

            var list = items
                .Where(x => !string.IsNullOrWhiteSpace(x.PropertyDesc))
                .Select(x => x.PropertyDesc.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sb = new StringBuilder();

            // -------------------------
            // Single property
            // -------------------------
            if (list.Count == 1)
            {
                sb.Append("<p><b>Property Description:</b> ");
                sb.Append(H(list[0]));
                sb.Append("</p>");

                if (includeRefs)
                {
                    var first = items.FirstOrDefault();

                    var o = first?.ObjectionNo ?? "";
                    if (!string.IsNullOrWhiteSpace(o))
                        sb.Append($"<p><b>Objection No:</b> {H(o)}</p>");

                    var a = first?.AppealNo ?? "";
                    if (!string.IsNullOrWhiteSpace(a))
                        sb.Append($"<p><b>Appeal No:</b> {H(a)}</p>");
                }

                return sb.ToString();
            }

            // -------------------------
            // Multiple properties
            // -------------------------
            sb.Append("<p><b>Property Descriptions:</b></p>");
            sb.Append("<ul style=\"margin-top:6px;\">");
            foreach (var p in list)
            {
                sb.Append("<li>");
                sb.Append(H(p));
                sb.Append("</li>");
            }
            sb.Append("</ul>");

            if (includeRefs)
            {
                var obs = items.Select(x => x.ObjectionNo)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (obs.Count > 0)
                {
                    sb.Append("<p><b>Objection numbers attached:</b></p><ul>");
                    foreach (var o in obs) sb.Append($"<li>{H(o!)}</li>");
                    sb.Append("</ul>");
                }

                var appeals = items.Select(x => x.AppealNo)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (appeals.Count > 0)
                {
                    sb.Append("<p><b>Appeal numbers attached:</b></p><ul>");
                    foreach (var a in appeals) sb.Append($"<li>{H(a!)}</li>");
                    sb.Append("</ul>");
                }
            }

            return sb.ToString();
        }
        private string EnquiriesBlock()
        {
            var e = _opt.Enquiries;
            var mail = _opt.Enquiries.Email;

            return "<p><b>For enquiries please contact:</b><br/>" +
                   $"Telephone {H(e.Tel1)} or {H(e.Tel2)}<br/>" +
                   $"Email: <a href=\"mailto:{H(mail)}\">{H(mail)}</a></p>";
        }

        private string SignOffBlock()
        {
            var s = _opt.SignOff;
            return $"<br/>{H(s.Line1)}<br/>{H(s.Line2)}<br/>{H(s.Line3)}";
        }

        private static string Subject(NoticeEmailRequest req, string label)
            => $"{label} - {req.RollShortCode}".Trim();

        private static string H(string input) => WebUtility.HtmlEncode(input ?? "");
    }
}