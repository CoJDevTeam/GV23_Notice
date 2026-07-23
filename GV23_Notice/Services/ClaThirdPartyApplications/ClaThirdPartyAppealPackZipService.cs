using GV23_Notice.Domain.Workflow.Entities;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace GV23_Notice.Services.ClaThirdPartyApplications
{
    /*
     * The interface/class names are kept for compatibility with the
     * existing dependency injection and print service.
     *
     * This service now builds a CLA Pack ZIP. It no longer looks up an
     * Appeal_No and no longer reads C:\AppealData.
     */
    public sealed class ClaThirdPartyAppealPackZipService
        : IClaThirdPartyAppealPackZipService
    {
        private readonly IConfiguration _config;

        public ClaThirdPartyAppealPackZipService(
            IConfiguration config)
        {
            _config = config;
        }

        public async Task<string> BuildAppealPackZipAsync(
            NoticeSettings settings,
            ClaThirdPartyApplicationNotice notice,
            string outputFolder,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(notice);

            if (string.IsNullOrWhiteSpace(notice.ClaNumber))
            {
                throw new InvalidOperationException(
                    "CLA number is required to build the CLA Pack ZIP.");
            }

            if (string.IsNullOrWhiteSpace(
                    notice.PropertyDescription))
            {
                throw new InvalidOperationException(
                    $"Property description is required to find the CLA Pack " +
                    $"for '{notice.ClaNumber}'.");
            }

            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                throw new InvalidOperationException(
                    "Output folder is required to build the CLA Pack ZIP.");
            }

            var claPackFolder =
                ResolveClaPackFolder(notice);

            Directory.CreateDirectory(outputFolder);

            var zipFileName =
                $"CLA Pack_{SafeFile(notice.ClaNumber)}.zip";

            var zipPath =
                Path.Combine(
                    outputFolder,
                    zipFileName);

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            await Task.Run(
                () => CreateZip(
                    claPackFolder,
                    zipPath,
                    ct),
                ct);

            if (!File.Exists(zipPath))
            {
                throw new IOException(
                    $"The CLA Pack ZIP was not created: {zipPath}");
            }

            /*
             * Keep using the existing database columns for compatibility.
             * AppealPackPath now stores the original CLA Pack source folder.
             * AppealPackZipPath stores the generated CLA Pack ZIP.
             */
            notice.AppealPackPath =
                claPackFolder;

            notice.AppealPackZipPath =
                zipPath;

            notice.AppealPackFileName =
                Path.GetFileName(zipPath);

            notice.AppealPackExists =
                true;

            return zipPath;
        }

        private string ResolveClaPackFolder(
            ClaThirdPartyApplicationNotice notice)
        {
            var root =
                _config[
                    "Storage:CLAThirdPartyAppealApplication:ClaPackRootPath"];

            if (string.IsNullOrWhiteSpace(root))
            {
                throw new InvalidOperationException(
                    "Storage:CLAThirdPartyAppealApplication:" +
                    "ClaPackRootPath is missing from appsettings.json.");
            }

            root = root.Trim();

            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException(
                    $"The configured CLA Pack root folder was not found: {root}");
            }

            var claToken =
                NormaliseForMatch(
                    notice.ClaNumber);

            var propertyWithoutCla =
                RemoveLeadingClaReference(
                    notice.ClaNumber!,
                    notice.PropertyDescription!);

            var propertyToken =
                NormaliseForMatch(
                    propertyWithoutCla);

            var fullExpectedToken =
                claToken + propertyToken;

            var folders = Directory
                .EnumerateDirectories(
                    root,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .Select(path => new
                {
                    Path = path,
                    Name = Path.GetFileName(path),
                    Token =
                        NormaliseForMatch(
                            Path.GetFileName(path))
                })
                .ToList();

            /*
             * Preferred match:
             * Folder name normalises exactly to:
             * CLA number + Property Description.
             *
             * Example:
             * CLA-104- ERF 81 DOORNFONTEIN-GV23_
             */
            var exactMatches = folders
                .Where(folder =>
                    folder.Token == fullExpectedToken)
                .ToList();

            if (exactMatches.Count == 1)
            {
                return exactMatches[0].Path;
            }

            if (exactMatches.Count > 1)
            {
                throw BuildDuplicateFolderException(
                    notice,
                    exactMatches.Select(x => x.Path));
            }

            /*
             * Fallback:
             * The folder must begin with the CLA number and contain the
             * normalised Property Description. This tolerates underscores,
             * spaces, hyphens, slashes and punctuation differences.
             */
            var compatibleMatches = folders
                .Where(folder =>
                    folder.Token.StartsWith(
                        claToken,
                        StringComparison.OrdinalIgnoreCase)
                    &&
                    (
                        string.IsNullOrWhiteSpace(propertyToken)
                        ||
                        folder.Token.Contains(
                            propertyToken,
                            StringComparison.OrdinalIgnoreCase)
                    ))
                .ToList();

            if (compatibleMatches.Count == 1)
            {
                return compatibleMatches[0].Path;
            }

            if (compatibleMatches.Count > 1)
            {
                throw BuildDuplicateFolderException(
                    notice,
                    compatibleMatches.Select(x => x.Path));
            }

            var expectedDisplay =
                $"{notice.ClaNumber}-" +
                $"{propertyWithoutCla}";

            throw new DirectoryNotFoundException(
                $"No CLA Pack folder was found for " +
                $"CLA '{notice.ClaNumber}' and Property Description " +
                $"'{notice.PropertyDescription}'. " +
                $"Expected a folder similar to '{expectedDisplay}' under " +
                $"'{root}'.");
        }

        private static Exception BuildDuplicateFolderException(
            ClaThirdPartyApplicationNotice notice,
            IEnumerable<string> matchingFolders)
        {
            var paths =
                string.Join(
                    Environment.NewLine,
                    matchingFolders.Select(path => $" - {path}"));

            return new InvalidOperationException(
                $"More than one CLA Pack folder matched " +
                $"CLA '{notice.ClaNumber}' and Property Description " +
                $"'{notice.PropertyDescription}':" +
                Environment.NewLine +
                paths);
        }

        private static void CreateZip(
            string sourceFolder,
            string zipPath,
            CancellationToken ct)
        {
            var files = Directory
                .EnumerateFiles(
                    sourceFolder,
                    "*",
                    SearchOption.AllDirectories)
                .Where(file =>
                    !file.EndsWith(
                        ".zip",
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (files.Count == 0)
            {
                throw new InvalidOperationException(
                    $"The CLA Pack folder contains no files: {sourceFolder}");
            }

            using var archive =
                ZipFile.Open(
                    zipPath,
                    ZipArchiveMode.Create);

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                var relativePath =
                    Path.GetRelativePath(
                        sourceFolder,
                        file);

                archive.CreateEntryFromFile(
                    file,
                    relativePath,
                    CompressionLevel.Optimal);
            }
        }

        private static string RemoveLeadingClaReference(
            string claNumber,
            string propertyDescription)
        {
            var value =
                propertyDescription
                    .Trim()
                    .TrimEnd(
                        '/',
                        '\\',
                        '_',
                        '-',
                        ' ');

            var escapedCla =
                Regex.Escape(
                    claNumber.Trim());

            value = Regex.Replace(
                value,
                $"^\\s*{escapedCla}\\s*[-_:/\\\\]*\\s*",
                string.Empty,
                RegexOptions.IgnoreCase);

            return value.Trim();
        }

        private static string NormaliseForMatch(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return new string(
                value
                    .ToUpperInvariant()
                    .Where(char.IsLetterOrDigit)
                    .ToArray());
        }

        private static string SafeFile(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Unknown";
            }

            var invalid =
                Path.GetInvalidFileNameChars();

            return new string(
                    value.Select(character =>
                        invalid.Contains(character)
                            ? '_'
                            : character)
                    .ToArray())
                .Trim();
        }
    }
}
