using GV23_Notice.Domain.Rolls;
using GV23_Notice.Domain.Storage;
using GV23_Notice.Domain.Workflow;
using Microsoft.Extensions.Options;
using System.Text;

namespace GV23_Notice.Services.Storage
{
    public sealed class NoticePathService : INoticePathService
    {
        private readonly StorageOptions _opt;

        public NoticePathService(IOptions<StorageOptions> opt)
        {
            _opt = opt.Value;
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
            bool isReview)
        {
            var root = GetRootPath(roll, NoticeKind.S52);
            var safeAppeal = SafeName(appealNo.Trim());
            var safeProp = SafeName(propertyDesc.Trim());

            var appealFolder = safeAppeal.Length > 0 ? safeAppeal : "Unknown_Appeal";
            var subFolder = isReview ? "Section 52 Review" : "Appeal Decision";
            var fileSuffix = isReview ? "S52" : "AD";
            var propPart = safeProp.Length > 0 ? safeProp : "Property";
            var fileName = $"{appealFolder}_{propPart}_{fileSuffix}.pdf";

            return Path.Combine(root, appealFolder, subFolder, fileName);
        }

        public string BuildS52EmlPath(
            RollRegistry roll,
            string appealNo,
            string propertyDesc,
            bool isReview)
        {
            var root = GetRootPath(roll, NoticeKind.S52);
            var safeAppeal = SafeName(appealNo.Trim());
            var safeProp = SafeName(propertyDesc.Trim());

            var appealFolder = safeAppeal.Length > 0 ? safeAppeal : "Unknown_Appeal";
            var subFolder = isReview ? "Section 52 Review" : "Appeal Decision";
            var fileSuffix = isReview ? "S52" : "AD";
            var propPart = safeProp.Length > 0 ? safeProp : "Property";
            var fileName = $"{appealFolder}_{propPart}_{fileSuffix}.eml";

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
    }
}