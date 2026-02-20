using System.Text;

namespace GV23_Notice.Services.Step3
{
    public sealed class WorkflowEmailReadService : IWorkflowEmailReadService
    {
        public (string Subject, string BodyHtml) TryReadEml(string? emlPath)
        {
            if (string.IsNullOrWhiteSpace(emlPath)) return ("", "");
            if (!File.Exists(emlPath)) return ("", "");

            try
            {
                var text = File.ReadAllText(emlPath, Encoding.UTF8);

                var subject = "";
                using (var sr = new StringReader(text))
                {
                    string? line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase))
                        {
                            subject = line.Substring("Subject:".Length).Trim();
                            break;
                        }
                    }
                }

                var idx = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (idx < 0) idx = text.IndexOf("\n\n", StringComparison.Ordinal);

                var body = idx >= 0 ? text.Substring(idx).Trim() : "";

                return (subject, body);
            }
            catch
            {
                return ("", "");
            }
        }
    }
}
