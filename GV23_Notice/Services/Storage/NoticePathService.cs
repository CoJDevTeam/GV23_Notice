using GV23_Notice.Domain.Rolls;
using GV23_Notice.Domain.Section49;
using GV23_Notice.Domain.Storage;
using GV23_Notice.Domain.Workflow;
using Microsoft.Extensions.Options;
using System.Text;

namespace GV23_Notice.Services.Storage
{
    public sealed class NoticePathService : INoticePathService
    {
        private readonly StorageOptions _opt;
        private readonly RollDbOptions _rollDb;
        private readonly Section49Options _section49;

        public NoticePathService(
            IOptions<StorageOptions> opt,
            IOptions<RollDbOptions> rollDb,
            IOptions<Section49Options> section49)
        {
            _opt = opt.Value;
            _rollDb = rollDb.Value;
            _section49 = section49.Value;
        }
        public string GetRootPath(RollRegistry roll, NoticeKind notice)
        {
            var key = (roll.ShortCode ?? "").Trim();

            // Appeals root for S52 (and if you treat S53 as appeal-related you can add it too)
            if (notice == NoticeKind.S52)
            {
                if (_opt.AppealRootsByShortCode.TryGetValue(key, out var p) && !string.IsNullOrWhiteSpace(p))
                    return p;

                throw new InvalidOperationException($"Appeal root path not configured for roll '{key}'.");
            }

            // All others use Objection roots
            if (_opt.ObjectionRootsByShortCode.TryGetValue(key, out var root) && !string.IsNullOrWhiteSpace(root))
                return root;

            throw new InvalidOperationException($"Objection root path not configured for roll '{key}'.");
        }

        public string BuildPdfPath(RollRegistry roll, NoticeKind notice, string keyNo, string propertyDesc, string? variantSuffix = null)
        {
            keyNo = (keyNo ?? "").Trim();
            propertyDesc = (propertyDesc ?? "").Trim();

            var root = GetRootPath(roll, notice);

            // sanitise
            var safeKey = SafeName(keyNo);
            var safeProp = SafeName(propertyDesc);

            // variantSuffix = "OWNER" / "REP" etc
            var suffix = string.IsNullOrWhiteSpace(variantSuffix) ? "" : $"_{SafeName(variantSuffix)}";

            // === RULES YOU SPECIFIED ===

            // S49:
            // root\{rollShortCode}_Section 49\{propertyDesc}\{propertyDesc}_Section49_Notice.pdf
            if (notice == NoticeKind.S49)
            {
                var main = $"{SafeName(roll.ShortCode)}_Section 49";
                var propFolder = safeProp.Length > 0 ? safeProp : "Unknown_Property";
                var file = $"{propFolder}_Section49_Notice{suffix}.pdf";
                return Path.Combine(root, main, propFolder, file);
            }

            // S51:
            // root\{ObjectionNo}\Section 51 Notice\{ObjectionNo}_{PropertyDesc}_S51.pdf
            if (notice == NoticeKind.S51)
            {
                var main = safeKey.Length > 0 ? safeKey : "Unknown_Objection";
                const string inner = "Section 51 Notice";
                var file = $"{main}_{safeProp}_S51{suffix}.pdf";
                return Path.Combine(root, main, inner, file);
            }

            // S52:
            // root\{AppealNo}\Section52_Notice\{AppealNo}_{PropertyDesc}_Section52_Notice.pdf
            if (notice == NoticeKind.S52)
            {
                var main = safeKey.Length > 0 ? safeKey : "Unknown_Appeal";
                var inner = "Section52_Notice";
                var file = $"{main}_{safeProp}_Section52_Notice{suffix}.pdf";
                return Path.Combine(root, main, inner, file);
            }

            // DJ + Invalid: “do as Section 51 using objection_No”
            if (notice == NoticeKind.DJ)
            {
                var main = safeKey.Length > 0 ? safeKey : "Unknown_Objection";
                var inner = "DearJohnny_Notice";
                var file = $"{main}_{safeProp}_DearJohnny_Notice{suffix}.pdf";
                return Path.Combine(root, main, inner, file);
            }

            if (notice == NoticeKind.IN)
            {
                var main = safeKey.Length > 0 ? safeKey : "Unknown_Objection";
                var inner = "Invalid_Notice";
                var file = $"{main}_{safeProp}_Invalid_Notice{suffix}.pdf";
                return Path.Combine(root, main, inner, file);
            }

            // S53 (you didn’t restate path here; using common pattern):
            // root\{ObjectionNo}\Section53_Notice\{ObjectionNo}_{PropertyDesc}_Section53_Notice.pdf
            if (notice == NoticeKind.S53)
            {
                var main = safeKey.Length > 0 ? safeKey : "Unknown_Objection";
                var inner = "Section53_Notice";
                var file = $"{main}_{safeProp}_Section53_Notice{suffix}.pdf";
                return Path.Combine(root, main, inner, file);
            }

            // fallback
            var fallbackMain = SafeName(roll.ShortCode) ?? "Roll";
            var fallbackFile = $"{safeKey}_{safeProp}_{notice}{suffix}.pdf";
            return Path.Combine(root, fallbackMain, fallbackFile);
        }

        // ── S52 direct paths ────────────────────────────────────────────────
        // root\{Appeal_No}\Section 52 Review\{Appeal_No}_{PropertyDesc}_S52.pdf
        // root\{Appeal_No}\Appeal Decision\{Appeal_No}_{PropertyDesc}_AD.pdf

        public string BuildS52PdfPath(
               RollRegistry roll,
               string appealNo,
               string propertyDesc,
               bool isReview,
               bool isPropOwner = false)
        {
            var root = GetRootPath(roll, NoticeKind.S52);
            var safeAppeal = SafeName(appealNo.Trim());
            var safeProp = SafeName(propertyDesc.Trim());

            var appealFolder = safeAppeal.Length > 0 ? safeAppeal : "Unknown_Appeal";
            var subFolder = isReview ? "Section 52 Review" : "Appeal Decision";
            var fileSuffix = isReview ? "S52" : "AD";
            var propPart = safeProp.Length > 0 ? safeProp : "Property";

            // Prop Owner copy gets _Prop_Owner suffix so it sits alongside the primary file
            var ownerSuffix = isPropOwner ? "_Prop_Owner" : "";
            var fileName = $"{appealFolder}_{propPart}_{fileSuffix}{ownerSuffix}.pdf";

            return Path.Combine(root, appealFolder, subFolder, fileName);
        }

        public string BuildS52EmlPath(
                  RollRegistry roll,
                  string appealNo,
                  string propertyDesc,
                  bool isReview,
                  bool isPropOwner = false)
        {
            var root = GetRootPath(roll, NoticeKind.S52);
            var safeAppeal = SafeName(appealNo.Trim());
            var safeProp = SafeName(propertyDesc.Trim());

            var appealFolder = safeAppeal.Length > 0 ? safeAppeal : "Unknown_Appeal";
            var subFolder = isReview ? "Section 52 Review" : "Appeal Decision";
            var fileSuffix = isReview ? "S52" : "AD";
            var propPart = safeProp.Length > 0 ? safeProp : "Property";

            var ownerSuffix = isPropOwner ? "_Prop_Owner" : "";
            var fileName = $"{appealFolder}_{propPart}_{fileSuffix}{ownerSuffix}.eml";

            return Path.Combine(root, appealFolder, subFolder, fileName);
        }


        public string BuildBatchPdfPath(
            RollRegistry roll,
            NoticeKind notice,
            string batchName,
            string propertyDesc,
            string? copyRole = null)
        {
            var root = GetRootPath(roll, notice);
            var safeRoll = SafeName(roll.ShortCode);
            var safeBatch = SafeName(batchName);
            var safeProp = SafeName(propertyDesc);
            var suffix = string.IsNullOrWhiteSpace(copyRole) ? "" : $"_{SafeName(copyRole)}";

            // Main folder:   {Roll}_{Notice}        e.g. SUPP_3_S49
            var noticeLabel = notice switch
            {
                NoticeKind.S49 => "S49",
                NoticeKind.S51 => "S51",
                NoticeKind.S52 => "S52",
                NoticeKind.S53 => "S53",
                NoticeKind.DJ => "DJ",
                NoticeKind.IN => "IN",
                NoticeKind.S78 => "S78",
                _ => notice.ToString()
            };

            var mainFolder = $"{safeRoll}_{noticeLabel}";
            var batchesFolder = "Batches";
            var batchFolder = safeBatch.Length > 0 ? safeBatch : "Unknown_Batch";
            var propPart = safeProp.Length > 0 ? safeProp : "Property";
            var fileName = $"{propPart}_{noticeLabel}{suffix}.pdf";

            // Full: root\{Roll}_{Notice}\Batches\{BatchName}\{PropertyDesc}_{Notice}.pdf
            return Path.Combine(root, mainFolder, batchesFolder, batchFolder, fileName);
        }


        public string BuildBatchEmlPath(
            RollRegistry roll,
            NoticeKind notice,
            string batchName,
            string propertyDesc)
        {
            var root = GetRootPath(roll, notice);
            var safeRoll = SafeName(roll.ShortCode);
            var safeBatch = SafeName(batchName);
            var safeProp = SafeName(propertyDesc);

            var noticeLabel = notice switch
            {
                NoticeKind.S49 => "S49",
                NoticeKind.S51 => "S51",
                NoticeKind.S52 => "S52",
                NoticeKind.S53 => "S53",
                NoticeKind.DJ => "DJ",
                NoticeKind.IN => "IN",
                NoticeKind.S78 => "S78",
                _ => notice.ToString()
            };

            // {root}\{Roll}_{Notice}\Batches\{BatchName}_Emails\{PropertyDesc}.eml
            var mainFolder = $"{safeRoll}_{noticeLabel}";
            var emailsFolder = $"{(safeBatch.Length > 0 ? safeBatch : "Batch")}_Emails";
            var fileName = $"{(safeProp.Length > 0 ? safeProp : "Property")}.eml";

            return Path.Combine(root, mainFolder, "Batches", emailsFolder, fileName);
        }

        public string BuildS51EmlPath(
            RollRegistry roll,
            string objectionNo,
            string propertyDesc)
        {
            var root = GetRootPath(roll, NoticeKind.S51);
            var safeKey = SafeName(objectionNo);
            var safeProp = SafeName(propertyDesc);

            // Same folder structure as the PDF:
            // {root}\{ObjectionNo}\Section 51 Notice\{ObjectionNo}_{PropertyDesc}.eml
            var objFolder = safeKey.Length > 0 ? safeKey : "Unknown_Objection";
            const string noticeFolder = "Section 51 Notice";
            var fileName = $"{objFolder}_{(safeProp.Length > 0 ? safeProp : "Property")}.eml";

            return Path.Combine(root, objFolder, noticeFolder, fileName);
        }


        public string BuildS53PdfPath(
            RollRegistry roll,
            string objectionNo,
            string propertyDesc,
            string? objectorType)
        {
            var root = GetRootPath(roll, NoticeKind.S53);
            var safeKey = SafeName(objectionNo);
            var safeProp = SafeName(propertyDesc);
            var objFolder = safeKey.Length > 0 ? safeKey : "Unknown_Objection";
            const string inner = "Section 53 MVD";

            // Filename suffix depends on which copy this row represents
            var fileName = objectorType switch
            {
                "Owner_Rep" => $"{objFolder}_{safeProp}_Owner_Rep_MVD.pdf",
                "Owner_Third_Party" => $"{objFolder}_{safeProp}_Owner_Third_Party_MVD.pdf",
                _ => $"{objFolder}_{safeProp}_MVD.pdf"   // Owner / Representative / Third_Party
            };

            return Path.Combine(root, objFolder, inner, fileName);
        }

        public string BuildS53EmlPath(
            RollRegistry roll,
            string objectionNo,
            string propertyDesc,
            string? objectorType)
        {
            var root = GetRootPath(roll, NoticeKind.S53);
            var safeKey = SafeName(objectionNo);
            var safeProp = SafeName(propertyDesc);
            var objFolder = safeKey.Length > 0 ? safeKey : "Unknown_Objection";
            const string inner = "Section 53 MVD";

            var fileName = objectorType switch
            {
                "Owner_Rep" => $"{objFolder}_{safeProp}_Owner_Rep_MVD.eml",
                "Owner_Third_Party" => $"{objFolder}_{safeProp}_Owner_Third_Party_MVD.eml",
                _ => $"{objFolder}_{safeProp}_MVD.eml"
            };

            return Path.Combine(root, objFolder, inner, fileName);
        }

        // ── Dear Johnny ──────────────────────────────────────────────────────
        // {root}\{ObjectionNo}\Dear Johnny Notice\{ObjectionNo}_{PropertyDesc}_DJ.pdf/.eml

        public string BuildDjPdfPath(RollRegistry roll, string objectionNo, string propertyDesc)
        {
            var root = GetRootPath(roll, NoticeKind.DJ);
            var safeKey = SafeName(objectionNo);
            var safeProp = SafeName(propertyDesc);
            var objFolder = safeKey.Length > 0 ? safeKey : "Unknown_Objection";
            const string inner = "Dear Johnny Notice";
            var fileName = $"{objFolder}_{(safeProp.Length > 0 ? safeProp : "Property")}_DJ.pdf";
            return Path.Combine(root, objFolder, inner, fileName);
        }

        public string BuildDjEmlPath(RollRegistry roll, string objectionNo, string propertyDesc)
        {
            var root = GetRootPath(roll, NoticeKind.DJ);
            var safeKey = SafeName(objectionNo);
            var safeProp = SafeName(propertyDesc);
            var objFolder = safeKey.Length > 0 ? safeKey : "Unknown_Objection";
            const string inner = "Dear Johnny Notice";
            var fileName = $"{objFolder}_{(safeProp.Length > 0 ? safeProp : "Property")}_DJ.eml";
            return Path.Combine(root, objFolder, inner, fileName);
        }

        // ── Invalid Notice ───────────────────────────────────────────────────
        // {root}\{ObjectionNo}\Invalid Notice\{ObjectionNo}_{PropertyDesc}_IO.pdf  (objection)
        // {root}\{ObjectionNo}\Invalid Notice\{ObjectionNo}_{PropertyDesc}_IOM.pdf (omission)

        public string BuildInPdfPath(RollRegistry roll, string objectionNo, string propertyDesc, bool isOmission)
        {
            var root = GetRootPath(roll, NoticeKind.IN);
            var safeKey = SafeName(objectionNo);
            var safeProp = SafeName(propertyDesc);
            var objFolder = safeKey.Length > 0 ? safeKey : "Unknown_Objection";
            const string inner = "Invalid Notice";
            var suffix = isOmission ? "IOM" : "IO";
            var fileName = $"{objFolder}_{(safeProp.Length > 0 ? safeProp : "Property")}_{suffix}.pdf";
            return Path.Combine(root, objFolder, inner, fileName);
        }

        public string BuildInEmlPath(RollRegistry roll, string objectionNo, string propertyDesc, bool isOmission)
        {
            var root = GetRootPath(roll, NoticeKind.IN);
            var safeKey = SafeName(objectionNo);
            var safeProp = SafeName(propertyDesc);
            var objFolder = safeKey.Length > 0 ? safeKey : "Unknown_Objection";
            const string inner = "Invalid Notice";
            var suffix = isOmission ? "IOM" : "IO";
            var fileName = $"{objFolder}_{(safeProp.Length > 0 ? safeProp : "Property")}_{suffix}.eml";
            return Path.Combine(root, objFolder, inner, fileName);
        }

        private static string SafeName(string? name)
        {
            name ??= "";
            name = name.Trim();

            if (name.Length == 0) return "";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);

            foreach (var ch in name)
            {
                if (invalid.Contains(ch)) sb.Append('_');
                else if (char.IsWhiteSpace(ch)) sb.Append('_');
                else sb.Append(ch);
            }

            // collapse "__"
            var s = sb.ToString();
            while (s.Contains("__")) s = s.Replace("__", "_");
            return s.Trim('_');
        }
        public string BuildS49PdfPath(
    RollRegistry roll,
    string propertyDesc)
        {
            var safeProperty =
                SafeName(propertyDesc);

            if (string.IsNullOrWhiteSpace(safeProperty))
                safeProperty = "Unknown_Property";

            // ----------------------------------------------------
            // Check whether this roll has the new configured
            // Section 49 storage.
            // ----------------------------------------------------
            if (!string.IsNullOrWhiteSpace(roll.SourceDb))
            {
                var source =
                    _rollDb.GetSource(
                        roll.SourceDb.Trim());

                var storage =
                    source.Section49?.Storage;

                if (
                    storage != null
                    &&
                    !string.IsNullOrWhiteSpace(
                        storage.PdfRootPath))
                {
                    var filePattern =
                        _section49
                            .StorageDefaults
                            .PdfFileNamePattern;

                    if (string.IsNullOrWhiteSpace(filePattern))
                    {
                        filePattern =
                            "Section49_{PropertyDesc}.pdf";
                    }

                    var fileName =
                        filePattern.Replace(
                            "{PropertyDesc}",
                            safeProperty,
                            StringComparison.OrdinalIgnoreCase);

                    fileName =
                        SafeName(fileName);

                    var root =
                        storage.PdfRootPath.Trim();

                    if (
                        _section49
                            .StorageDefaults
                            .CreatePropertyFolderForPdf)
                    {
                        return Path.Combine(
                            root,
                            safeProperty,
                            fileName);
                    }

                    return Path.Combine(
                        root,
                        fileName);
                }
            }

            // ----------------------------------------------------
            // Legacy fallback.
            // ----------------------------------------------------
            var oldRoot =
                GetRootPath(
                    roll,
                    NoticeKind.S49);

            var oldMain =
                $"{SafeName(roll.ShortCode)}_Section 49";

            var oldFile =
                $"{safeProperty}_Section49_Notice.pdf";

            return Path.Combine(
                oldRoot,
                oldMain,
                safeProperty,
                oldFile);
        }

        public string? GetS49SignaturePath(
     RollRegistry roll)
        {
            var rollShortCode =
                (roll.ShortCode ?? string.Empty)
                .Trim();

            if (string.IsNullOrWhiteSpace(rollShortCode))
                return null;

            /*
             * Signatures uploaded from Date Configuration are stored:
             *
             * {RollRoot}\Signature Folder\{RollShortCode}_S49\v1
             * {RollRoot}\Signature Folder\{RollShortCode}_S49\v2
             * ...
             *
             * Example SUPP4:
             * C:\Sup4\Signature Folder\SUPP 4_S49\v2
             */

            string rollRoot;

            try
            {
                rollRoot =
                    GetRootPath(
                        roll,
                        NoticeKind.S49);
            }
            catch
            {
                return null;
            }

            var signatureRoot =
                Path.Combine(
                    rollRoot,
                    "Signature Folder");

            if (!Directory.Exists(
                    signatureRoot))
            {
                return null;
            }

            var noticeFolder =
                Path.Combine(
                    signatureRoot,
                    $"{rollShortCode}_S49");

            if (!Directory.Exists(
                    noticeFolder))
            {
                return null;
            }

            /*
             * Find latest version:
             * v1
             * v2
             * v3
             */
            var latestVersionFolder =
                Directory
                    .EnumerateDirectories(
                        noticeFolder,
                        "v*",
                        SearchOption.TopDirectoryOnly)
                    .Select(path =>
                        new
                        {
                            Path = path,
                            Version =
                                ParseSignatureVersion(
                                    Path.GetFileName(path))
                        })
                    .Where(x =>
                        x.Version.HasValue)
                    .OrderByDescending(x =>
                        x.Version!.Value)
                    .Select(x =>
                        x.Path)
                    .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(
                    latestVersionFolder))
            {
                return null;
            }

            /*
             * Find the uploaded signature image inside latest version.
             */
            var allowedExtensions =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase)
                {
            ".png",
            ".jpg",
            ".jpeg",
            ".bmp"
                };

            var signatureFile =
                Directory
                    .EnumerateFiles(
                        latestVersionFolder,
                        "*.*",
                        SearchOption.AllDirectories)
                    .Where(file =>
                        allowedExtensions.Contains(
                            Path.GetExtension(file)))
                    .OrderBy(file =>
                        file)
                    .FirstOrDefault();

            return signatureFile;
        }

        private static int? ParseSignatureVersion(
            string? folderName)
        {
            if (string.IsNullOrWhiteSpace(
                    folderName))
            {
                return null;
            }

            var value =
                folderName.Trim();

            if (!value.StartsWith(
                    "v",
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var versionPart =
                value[1..];

            return int.TryParse(
                versionPart,
                out var version)
                    ? version
                    : null;
        }
    }
}