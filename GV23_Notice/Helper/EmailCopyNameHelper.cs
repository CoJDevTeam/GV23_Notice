// Helpers/EmailCopyNameHelper.cs

namespace GV23_Notice.Helpers
{
    public static class EmailCopyNameHelper
    {
        public static string BuildEmlFileName(
            string notice,
            string? objectionNo,
            string? appealNo,
            string? propertyDesc,
            string? recipientEmail,
            DateTime? now = null)
        {
            var stamp = (now ?? DateTime.Now).ToString("yyyyMMdd_HHmmss");

            var email = CleanFilePart(recipientEmail);
            if (string.IsNullOrWhiteSpace(email))
                email = "NoEmail";

            string key;

            if (notice.Equals("S49", StringComparison.OrdinalIgnoreCase))
            {
                key = CleanFilePart(propertyDesc);

                if (string.IsNullOrWhiteSpace(key))
                    key = "Property";
            }
            else
            {
                key = CleanFilePart(objectionNo);

                if (string.IsNullOrWhiteSpace(key))
                    key = CleanFilePart(appealNo);

                if (string.IsNullOrWhiteSpace(key))
                    key = CleanFilePart(propertyDesc);

                if (string.IsNullOrWhiteSpace(key))
                    key = "Notice";
            }

            return $"email_{key}_{email}_{stamp}.eml";
        }

        public static string CleanFilePart(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            var clean = value.Trim();

            foreach (var c in Path.GetInvalidFileNameChars())
                clean = clean.Replace(c, '_');

            clean = clean
                .Replace(" ", "_")
                .Replace("/", "_")
                .Replace("\\", "_")
                .Replace(":", "_")
                .Replace("*", "_")
                .Replace("?", "_")
                .Replace("\"", "_")
                .Replace("<", "_")
                .Replace(">", "_")
                .Replace("|", "_");

            while (clean.Contains("__"))
                clean = clean.Replace("__", "_");

            return clean.Trim('_');
        }
    }
}