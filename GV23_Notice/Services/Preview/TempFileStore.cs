namespace GV23_Notice.Services.Preview
{
    public sealed class TempFileStore : ITempFileStore
    {
        private readonly IWebHostEnvironment _env;

        public TempFileStore(IWebHostEnvironment env) => _env = env;

        public async Task<string> SavePdfAsync(byte[] pdfBytes, string fileName, CancellationToken ct)
        {
            if (pdfBytes is null || pdfBytes.Length == 0)
                throw new ArgumentException("Empty PDF bytes.", nameof(pdfBytes));

            fileName = SanitizeFileName(string.IsNullOrWhiteSpace(fileName) ? "preview.pdf" : fileName);
            if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                fileName += ".pdf";

            var token = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}";
            var root = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var folder = Path.Combine(root, "temp");
            Directory.CreateDirectory(folder);

            var fullPath = Path.Combine(folder, $"{token}_{fileName}");
            await File.WriteAllBytesAsync(fullPath, pdfBytes, ct);

            // URL your iframe can load
            return $"/temp/{Uri.EscapeDataString(token)}_{Uri.EscapeDataString(fileName)}";
        }

        public Task<(byte[] Bytes, string ContentType, string FileName)?> TryGetAsync(string token, CancellationToken ct)
        {
            // Optional if you later want a controller endpoint like /Workflow/TempFile?token=...
            return Task.FromResult<(byte[] Bytes, string ContentType, string FileName)?>(null);
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }
    }
}
