using System.Text;
using System.Text.Json;

namespace GV23_Notice.Services.Email
{
    public sealed class NoticeEmailArchiveService : INoticeEmailArchiveService
    {
        private readonly IStorageRootResolver _root;

        public NoticeEmailArchiveService(IStorageRootResolver root)
        {
            _root = root;
        }


        public async Task<string> SaveAsync(
            int rollId,
            DataDomain domain,                 // ✅ required (Objection vs Appeal)
            string rollShortCode,              // optional (kept for meta/file naming)
            string notice,                     // optional (kept for meta/file naming)
            int version,
            string category,                   // "Approval" | "Correction"
            string fileStem,                   // e.g. $"Step1_{notice}_Settings_{settingsId}"
            string subject,
            string bodyHtml,
            object meta,
            CancellationToken ct = default)
        {
            // ✅ Get the correct roll root using your resolver
            var rollRoot = await _root.GetRootAsync(rollId, domain, ct);

            // ✅ Folder: {rollRoot}\Notice Email approval_Request\{category}\V{version}
            var folder = Path.Combine(
                rollRoot,
                "Notice Email approval_Request",
                category,
                $"V{version}");

            Directory.CreateDirectory(folder);

            var safeStem = MakeSafeFileName(fileStem);
            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var htmlPath = Path.Combine(folder, $"{ts}_{safeStem}.html");
            var jsonPath = Path.Combine(folder, $"{ts}_{safeStem}.json");

            // ✅ HTML (wrap with subject + consistent template)
            await File.WriteAllTextAsync(htmlPath, WrapHtml(subject, bodyHtml), Encoding.UTF8, ct);

            // ✅ JSON snapshot (include useful top-level info too)
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

            return htmlPath;
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

