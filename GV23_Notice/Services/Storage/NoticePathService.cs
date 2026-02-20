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
            // root\{ObjectionNo}\Section51_Notice\{ObjectionNo}_{PropertyDesc}_Section51_Notice.pdf
            if (notice == NoticeKind.S51)
            {
                var main = safeKey.Length > 0 ? safeKey : "Unknown_Objection";
                var inner = "Section51_Notice";
                var file = $"{main}_{safeProp}_Section51_Notice{suffix}.pdf";
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
