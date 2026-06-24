using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace GV23_Notice.Services.Email
{
    public sealed class NoticeEmailArchiveService : INoticeEmailArchiveService
    {
        private readonly IStorageRootResolver _root;
        private readonly IConfiguration _config;

        public NoticeEmailArchiveService(
            IStorageRootResolver root,
            IConfiguration config)
        {
            _root = root;
            _config = config;
        }

        public async Task<string> SaveAsync(
            int rollId,
            DataDomain domain,
            string rollShortCode,
            string notice,
            int version,
            string category,
            string fileStem,
            string subject,
            string bodyHtml,
            object meta,
            CancellationToken ct = default)
        {
            var rollRoot = await _root.GetRootAsync(rollId, domain, ct);

            var folder = Path.Combine(
                rollRoot,
                "Notice Email approval_Request",
                category,
                $"V{version}");

            Directory.CreateDirectory(folder);

            //var safeStem = MakeSafeFileName(fileStem);
            var safeStem = MakeSafeFileName(
    string.IsNullOrWhiteSpace(fileStem)
        ? $"{notice}_{category}"
        : fileStem);
            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var htmlPath = Path.Combine(folder, $"{ts}_{safeStem}.html");
            var jsonPath = Path.Combine(folder, $"{ts}_{safeStem}.json");
            var emlPath = Path.Combine(folder, $"{ts}_{safeStem}.eml");

            await File.WriteAllTextAsync(htmlPath, WrapHtml(subject, bodyHtml), Encoding.UTF8, ct);

            var metaEnvelope = new
            {
                rollId,
                domain = domain.ToString(),
                rollShortCode,
                notice,
                version,
                category,
                subject,
                savedAt = DateTime.Now,
                meta
            };

            var json = JsonSerializer.Serialize(metaEnvelope, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(jsonPath, json, Encoding.UTF8, ct);

            // ✅ write .eml
            var archiveToAddress = _config["NoticeEmailArchive:ArchiveToAddress"];

            if (string.IsNullOrWhiteSpace(archiveToAddress))
                archiveToAddress = "archive@joburg.org.za";

            var archiveToName = _config["NoticeEmailArchive:ArchiveToName"] ?? "GV23 Notice Archive";

            SaveAsEml(subject, bodyHtml, emlPath, archiveToAddress, archiveToName);
            // return eml path (or html path if you prefer)
            return emlPath;
        }

        private static void SaveAsEml(
    string subject,
    string bodyHtml,
    string emlPath,
    string archiveToAddress,
    string? archiveToName)
        {
            using var msg = new MailMessage
            {
                Subject = subject,
                Body = bodyHtml,
                IsBodyHtml = true
            };

            // Placeholder From/To for archive file formatting only.
            // This does NOT send the real notice to this address.
            msg.From = new MailAddress("no-reply@joburg.org.za", "GV23 Workflow");

            if (string.IsNullOrWhiteSpace(archiveToAddress))
                archiveToAddress = "archive@joburg.org.za";

            msg.To.Add(new MailAddress(
                archiveToAddress.Trim(),
                string.IsNullOrWhiteSpace(archiveToName) ? "GV23 Notice Archive" : archiveToName.Trim()));

            var pickupDir = Path.GetDirectoryName(emlPath)!;
            Directory.CreateDirectory(pickupDir);

            using var client = new SmtpClient
            {
                DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
                PickupDirectoryLocation = pickupDir
            };

            client.Send(msg);

            var newest = new DirectoryInfo(pickupDir)
                .GetFiles("*.eml")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (newest is null)
                return;

            if (File.Exists(emlPath))
                File.Delete(emlPath);

            newest.MoveTo(emlPath);
        }

        private static string WrapHtml(string subject, string bodyHtml)
        {
            return $"""
<!doctype html>
<html>
<head>
  <meta charset="utf-8" />
  <title>{System.Net.WebUtility.HtmlEncode(subject)}</title>
</head>
<body>
{bodyHtml}
</body>
</html>
""";
        }

        private static string MakeSafeFileName(string s)
        {
            s ??= "email";
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s.Trim().Replace(" ", "_");
        }
    }
}


