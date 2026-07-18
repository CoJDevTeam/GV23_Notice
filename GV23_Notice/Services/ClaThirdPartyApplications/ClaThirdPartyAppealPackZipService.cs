using GV23_Notice.Domain.Workflow.Entities;
using System.IO.Compression;

namespace GV23_Notice.Services.ClaThirdPartyApplications
{
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
                    "CLA number is required to build the appeal pack.");
            }

            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                throw new InvalidOperationException(
                    "Output folder is required to build the appeal pack.");
            }

            var appealPackFolder = ResolveAppealPackFolder(
                settings,
                notice);

            if (!Directory.Exists(appealPackFolder))
            {
                throw new DirectoryNotFoundException(
                    $"CLA appeal-pack folder was not found: {appealPackFolder}");
            }

            Directory.CreateDirectory(outputFolder);

            var zipFileName =
                $"Appeal Pack_{SafeFile(notice.ClaNumber)}.zip";

            var zipPath = Path.Combine(
                outputFolder,
                zipFileName);

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            await Task.Run(
                () => CreateZip(
                    appealPackFolder,
                    zipPath,
                    outputFolder,
                    ct),
                ct);

            if (!File.Exists(zipPath))
            {
                throw new IOException(
                    $"The CLA appeal-pack ZIP file was not created: {zipPath}");
            }

            return zipPath;
        }

        private string ResolveAppealPackFolder(
            NoticeSettings settings,
            ClaThirdPartyApplicationNotice notice)
        {
            /*
             * Preferred source:
             * A manually supplied appeal-pack folder for this CLA record.
             */
            if (!string.IsNullOrWhiteSpace(notice.AppealPackPath))
            {
                var configuredPath =
                    notice.AppealPackPath.Trim();

                if (Directory.Exists(configuredPath))
                {
                    return configuredPath;
                }

                if (File.Exists(configuredPath))
                {
                    return Path.GetDirectoryName(configuredPath)
                        ?? throw new InvalidOperationException(
                            $"The folder containing the appeal pack could not be resolved: {configuredPath}");
                }
            }

            /*
             * Fallback:
             * Resolve from the configured appeal root and CLA number.
             */
            var rootKey = ResolveRootKey(
                notice.RollShortCode,
                settings.Roll.ToString());

            var appealRoot =
                _config[
                    $"Storage:AppealRootsByShortCode:{rootKey}"];

            if (string.IsNullOrWhiteSpace(appealRoot))
            {
                throw new InvalidOperationException(
                    $"Appeal root folder is not configured for key '{rootKey}'.");
            }

            var claFolder = Path.Combine(
                appealRoot,
                SafeFile(notice.ClaNumber!));

            /*
             * Optional dedicated source folder:
             *
             * {CLA folder}\APPEAL PACK
             */
            var appealPackFolderName =
                _config[
                    "Storage:CLAThirdPartyAppealApplication:AppealPackFolderName"]
                ?? "APPEAL PACK";

            var dedicatedAppealPackFolder = Path.Combine(
                claFolder,
                appealPackFolderName.Trim());

            if (Directory.Exists(dedicatedAppealPackFolder))
            {
                return dedicatedAppealPackFolder;
            }

            /*
             * Final fallback:
             * Use the CLA folder itself, while excluding generated
             * CLA PDFs, email copies and ZIP files.
             */
            return claFolder;
        }

        private void CreateZip(
            string sourceFolder,
            string zipPath,
            string outputFolder,
            CancellationToken ct)
        {
            var claPdfFolderName =
                _config[
                    "Storage:CLAThirdPartyAppealApplication:PdfFolderName"]
                ?? "CLA-THIRD-PARTY APPLICATION";

            var emailCopyFolderName =
                _config[
                    "Storage:CLAThirdPartyAppealApplication:EmailCopyFolderName"]
                ?? "CLA EMAIL COPIES";

            var normalisedOutputFolder =
                Path.GetFullPath(outputFolder)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

            var files = Directory
                .EnumerateFiles(
                    sourceFolder,
                    "*.*",
                    SearchOption.AllDirectories)
                .Where(file =>
                    ShouldIncludeFile(
                        file,
                        zipPath,
                        normalisedOutputFolder,
                        claPdfFolderName,
                        emailCopyFolderName))
                .ToList();

            if (files.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No appeal-pack files were found in: {sourceFolder}");
            }

            using var archive = ZipFile.Open(
                zipPath,
                ZipArchiveMode.Create);

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(
                    sourceFolder,
                    file);

                archive.CreateEntryFromFile(
                    file,
                    relativePath,
                    CompressionLevel.Optimal);
            }
        }

        private static bool ShouldIncludeFile(
            string filePath,
            string zipPath,
            string outputFolder,
            string claPdfFolderName,
            string emailCopyFolderName)
        {
            var fullFilePath = Path.GetFullPath(filePath);

            if (string.Equals(
                    fullFilePath,
                    Path.GetFullPath(zipPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (filePath.EndsWith(
                    ".zip",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var fileDirectory =
                Path.GetDirectoryName(fullFilePath) ?? "";

            /*
             * Do not include generated files from the output folder.
             */
            if (IsSameOrChildFolder(
                    fileDirectory,
                    outputFolder))
            {
                return false;
            }

            if (ContainsFolder(
                    fullFilePath,
                    claPdfFolderName))
            {
                return false;
            }

            if (ContainsFolder(
                    fullFilePath,
                    emailCopyFolderName))
            {
                return false;
            }

            return true;
        }

        private static bool ContainsFolder(
            string path,
            string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
            {
                return false;
            }

            var separator =
                Path.DirectorySeparatorChar;

            var alternateSeparator =
                Path.AltDirectorySeparatorChar;

            var normalisedPath = path
                .Replace(
                    alternateSeparator,
                    separator);

            var normalisedFolder = folderName
                .Trim()
                .Trim(
                    separator,
                    alternateSeparator);

            return normalisedPath.Contains(
                $"{separator}{normalisedFolder}{separator}",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSameOrChildFolder(
            string candidate,
            string parent)
        {
            var normalisedCandidate = Path
                .GetFullPath(candidate)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            var normalisedParent = Path
                .GetFullPath(parent)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            return normalisedCandidate.Equals(
                       normalisedParent,
                       StringComparison.OrdinalIgnoreCase)
                   ||
                   normalisedCandidate.StartsWith(
                       normalisedParent +
                       Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveRootKey(
            string? recordShortCode,
            string? settingsRoll)
        {
            var value = FirstNonEmpty(
                    recordShortCode,
                    settingsRoll,
                    "GV23")
                .Replace(" ", "")
                .Trim()
                .ToUpperInvariant();

            return value switch
            {
                "SUPP1" => "SUPP 1",
                "SUPP2" => "SUPP 2",
                "SUPP3" => "SUPP 3",
                _ => "GV23"
            };
        }

        private static string FirstNonEmpty(
            params string?[] values)
        {
            return values
                .FirstOrDefault(
                    value =>
                        !string.IsNullOrWhiteSpace(value))
                ?.Trim()
                ?? "";
        }

        private static string SafeFile(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Unknown";
            }

            var invalidCharacters =
                Path.GetInvalidFileNameChars();

            var cleaned = new string(
                value
                    .Select(character =>
                        invalidCharacters.Contains(character)
                            ? '_'
                            : character)
                    .ToArray());

            return cleaned.Trim();
        }
    }
}