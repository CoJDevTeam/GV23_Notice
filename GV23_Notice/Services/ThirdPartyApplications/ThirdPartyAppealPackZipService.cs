using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow.Entities;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;

namespace GV23_Notice.Services.ThirdPartyApplications
{
    public sealed class ThirdPartyAppealPackZipService : IThirdPartyAppealPackZipService
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;

        public ThirdPartyAppealPackZipService(
            AppDbContext db,
            IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        public async Task<string> BuildAppealPackZipAsync(
            NoticeSettings settings,
            string appealNo,
            string outputFolder,
            CancellationToken ct)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (string.IsNullOrWhiteSpace(appealNo))
                throw new InvalidOperationException("Appeal number is required to build the appeal pack.");

            if (string.IsNullOrWhiteSpace(outputFolder))
                throw new InvalidOperationException("Output folder is required to build the appeal pack.");

            var roll = await _db.RollRegistry
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.RollId == settings.RollId, ct);

            if (roll == null)
                throw new InvalidOperationException($"Roll not found for RollId {settings.RollId}.");

            var root = ResolveAppealRoot(roll.ShortCode);

            if (string.IsNullOrWhiteSpace(root))
                throw new InvalidOperationException($"Appeal root folder is not configured for roll '{roll.ShortCode}'.");

            var appealFolder = Path.Combine(root, appealNo);

            if (!Directory.Exists(appealFolder))
                throw new DirectoryNotFoundException($"Appeal pack folder was not found: {appealFolder}");

            Directory.CreateDirectory(outputFolder);

            var zipPath = Path.Combine(
                outputFolder,
                $"Appeal Pack_{SafeFile(appealNo)}.zip");

            if (File.Exists(zipPath))
                File.Delete(zipPath);

            await Task.Run(() =>
            {
                using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

                var files = Directory
                    .EnumerateFiles(appealFolder, "*.*", SearchOption.AllDirectories)
                    .Where(x =>
                        !x.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                        !x.Contains($"{Path.DirectorySeparatorChar}THIRD-PARTY APPLICATION{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();

                    var relative = Path.GetRelativePath(appealFolder, file);

                    archive.CreateEntryFromFile(
                        file,
                        relative,
                        CompressionLevel.Optimal);
                }
            }, ct);

            return zipPath;
        }

        private string ResolveAppealRoot(string? rollShortCode)
        {
            var key = string.IsNullOrWhiteSpace(rollShortCode)
                ? ""
                : rollShortCode.Trim();

            var direct = _config[$"Storage:AppealRootsByShortCode:{key}"];

            if (!string.IsNullOrWhiteSpace(direct))
                return direct;

            var noSpace = key.Replace(" ", "");
            var fallback = _config[$"Storage:AppealRootsByShortCode:{noSpace}"];

            return fallback ?? "";
        }

        private static string SafeFile(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unknown";

            var invalid = Path.GetInvalidFileNameChars();

            var cleaned = new string(
                value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());

            return cleaned.Trim();
        }
    }
}