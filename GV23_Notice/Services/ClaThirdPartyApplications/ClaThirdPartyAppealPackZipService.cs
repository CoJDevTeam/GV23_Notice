using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow.Entities;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.IO.Compression;

namespace GV23_Notice.Services.ClaThirdPartyApplications
{
    public sealed class ClaThirdPartyAppealPackZipService
        : IClaThirdPartyAppealPackZipService
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;

        public ClaThirdPartyAppealPackZipService(
            AppDbContext db,
            IConfiguration config)
        {
            _db = db;
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

            var appealNo = await ResolveAppealNumberAsync(
                notice.ObjectionNumber,
                ct);

            var appealPackFolder = ResolveAppealPackFolder(
                settings,
                notice,
                appealNo);

            if (!Directory.Exists(appealPackFolder))
            {
                throw new DirectoryNotFoundException(
                    $"Appeal-pack folder was not found for Objection_No " +
                    $"'{notice.ObjectionNumber}' and Appeal_No '{appealNo}': " +
                    appealPackFolder);
            }

            Directory.CreateDirectory(outputFolder);

            var zipFileName =
                $"Appeal Pack_{SafeFile(appealNo)}.zip";

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
            ClaThirdPartyApplicationNotice notice,
            string appealNo)
        {
            var rootKey = ResolveRootKeyFromAppealNumber(
                appealNo,
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

            var appealFolder = Path.Combine(
                appealRoot,
                SafeFile(appealNo));

            var appealPackFolderName =
                _config[
                    "Storage:CLAThirdPartyAppealApplication:AppealPackFolderName"]
                ?? "APPEAL PACK";

            var dedicatedAppealPackFolder = Path.Combine(
                appealFolder,
                appealPackFolderName.Trim());

            if (Directory.Exists(dedicatedAppealPackFolder))
            {
                return dedicatedAppealPackFolder;
            }

            return appealFolder;
        }

        private async Task<string> ResolveAppealNumberAsync(
            string? objectionNo,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(objectionNo))
            {
                throw new InvalidOperationException(
                    "Objection_No is required to resolve the Appeal_No.");
            }

            /*
             * Obj_Property_Info_Appeal is not in GV23_Notice.
             * It is stored in:
             *
             * [Objection].[dbo].[Obj_Property_Info_Appeal]
             *
             * The CLA table ObjectionNumber maps directly to Obj_Ref.
             */
            var connection = _db.Database.GetDbConnection();

            var shouldClose =
                connection.State != ConnectionState.Open;

            if (shouldClose)
            {
                await connection.OpenAsync(ct);
            }

            try
            {
                await using var command =
                    connection.CreateCommand();

                command.CommandText = """
                    SELECT TOP (1)
                        LTRIM(RTRIM(
                            CONVERT(NVARCHAR(200), Appeal_No)
                        ))
                    FROM [Objection].[dbo].[Obj_Property_Info_Appeal]
                    WHERE LTRIM(RTRIM(
                              CONVERT(NVARCHAR(200), Obj_Ref)
                          )) = @ObjectionNo
                      AND Appeal_No IS NOT NULL
                      AND LTRIM(RTRIM(
                              CONVERT(NVARCHAR(200), Appeal_No)
                          )) <> N''
                    ORDER BY Appeal_ID DESC;
                    """;

                var parameter =
                    command.CreateParameter();

                parameter.ParameterName =
                    "@ObjectionNo";

                parameter.Value =
                    objectionNo.Trim();

                command.Parameters.Add(
                    parameter);

                var value =
                    await command.ExecuteScalarAsync(ct);

                var appealNo =
                    value?.ToString()?.Trim();

                if (string.IsNullOrWhiteSpace(appealNo))
                {
                    throw new InvalidOperationException(
                        $"No Appeal_No was found in " +
                        $"[Objection].[dbo].[Obj_Property_Info_Appeal] " +
                        $"where Obj_Ref = '{objectionNo}'.");
                }

                return appealNo;
            }
            finally
            {
                if (shouldClose)
                {
                    await connection.CloseAsync();
                }
            }
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

        private static string ResolveRootKeyFromAppealNumber(
            string appealNo,
            string? recordShortCode,
            string? settingsRoll)
        {
            var value = appealNo?.Trim() ?? "";

            if (value.Contains(
                    "APP-GV23-Sup3-",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "SUPP 3";
            }

            if (value.Contains(
                    "APP-GV23-Sup2-",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "SUPP 2";
            }

            if (value.Contains(
                    "APP-GV23-Sup1-",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "SUPP 1";
            }

            if (value.Contains(
                    "APP-GV23-",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "GV23";
            }

            var fallback = FirstNonEmpty(
                    recordShortCode,
                    settingsRoll,
                    "GV23")
                .Replace(" ", "")
                .Trim()
                .ToUpperInvariant();

            return fallback switch
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