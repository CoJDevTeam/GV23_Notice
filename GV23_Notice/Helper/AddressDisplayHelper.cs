// Helpers/AddressDisplayHelper.cs

using System.Text.RegularExpressions;

namespace GV23_Notice.Helpers
{
    public static class AddressDisplayHelper
    {
        public static AddressLines Format(
            string? addr1,
            string? addr2,
            string? addr3,
            string? addr4,
            string? addr5)
        {
            var raw = new List<string?>
            {
                Clean(addr1),
                Clean(addr2),
                Clean(addr3),
                Clean(addr4),
                Clean(addr5)
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Where(x => !IsBadAddressLine(x))
            .ToList();

            // Remove client / owner / representative name if ADDR1 is not really an address line.
            raw = RemoveNameLineIfPresent(raw);

            var result = new List<string>();

            for (var i = 0; i < raw.Count; i++)
            {
                var current = raw[i];

                if (i + 1 < raw.Count && ShouldCombineWithNext(current, raw[i + 1]))
                {
                    result.Add($"{current} {raw[i + 1]}".Trim());
                    i++;
                }
                else
                {
                    result.Add(current);
                }
            }

            while (result.Count < 5)
                result.Add("");

            return new AddressLines
            {
                Addr1 = ToUpperSafe(result.ElementAtOrDefault(0)),
                Addr2 = ToUpperSafe(result.ElementAtOrDefault(1)),
                Addr3 = ToUpperSafe(result.ElementAtOrDefault(2)),
                Addr4 = ToUpperSafe(result.ElementAtOrDefault(3)),
                Addr5 = ToUpperSafe(result.ElementAtOrDefault(4))
            };
        }
        private static string ToUpperSafe(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? ""
                : value.Trim().ToUpperInvariant();
        }
        private static List<string> RemoveNameLineIfPresent(List<string> lines)
        {
            if (lines.Count < 2)
                return lines;

            var first = lines[0];
            var second = lines[1];

            // If ADDR1 is already a real address, keep it.
            // Examples:
            // 32
            // 32 ROOS
            // 12/1009
            // PO BOX 71121
            // 474 LYNNWOOD ROAD
            if (LooksLikeAddressLine(first))
                return lines;

            // If ADDR1 is an email, remove it.
            if (LooksLikeEmail(first))
                return lines.Skip(1).ToList();

            // If ADDR1 looks like a person/company name and ADDR2 starts the real address,
            // remove ADDR1.
            // Examples:
            // Jan-Paul Smit + 5 + Lynx Road
            // Sharmane Majendie-Kennedy + 913 + Touches Str
            // Balme Van Wyk & Tugman + 13 + Bruton Road
            if (LooksLikeNameOrCompany(first) && LooksLikeAddressLine(second))
                return lines.Skip(1).ToList();

            // Also remove ADDR1 if ADDR2 is a street number and ADDR3 is a street name.
            if (lines.Count >= 3 &&
                LooksLikeNameOrCompany(first) &&
                IsNumberOnlyAddressPart(second) &&
                HasText(lines[2]))
            {
                return lines.Skip(1).ToList();
            }

            return lines;
        }

        private static string? Clean(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var clean = value.Trim();

            while (clean.Contains("  "))
                clean = clean.Replace("  ", " ");

            return clean;
        }

        private static bool IsBadAddressLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;

            var v = value.Trim();

            return v.Equals("NULL", StringComparison.OrdinalIgnoreCase)
                || v.Equals("N/A", StringComparison.OrdinalIgnoreCase)
                || v.Equals("NA", StringComparison.OrdinalIgnoreCase)
                || v.Equals("-", StringComparison.OrdinalIgnoreCase)
                || v.Equals("0", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldCombineWithNext(string current, string next)
        {
            if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(next))
                return false;

            current = current.Trim();
            next = next.Trim();

            if (IsPoBoxOrPrivateBag(current))
                return false;

            if (HasNumberAndText(current))
                return false;

            return IsNumberOnlyAddressPart(current) && HasText(next);
        }

        private static bool LooksLikeAddressLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();

            if (IsPoBoxOrPrivateBag(value))
                return true;

            if (IsNumberOnlyAddressPart(value))
                return true;

            if (HasNumberAndText(value))
                return true;

            if (StartsWithAddressKeyword(value))
                return true;

            return false;
        }

        private static bool LooksLikeNameOrCompany(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();

            if (LooksLikeEmail(value))
                return true;

            if (LooksLikeAddressLine(value))
                return false;

            // Mostly letters/spaces/company symbols, no street number.
            return HasText(value);
        }

        private static bool IsPoBoxOrPrivateBag(string value)
        {
            return value.StartsWith("PO BOX", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("P O BOX", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("P.O BOX", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("P.O. BOX", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("PRIVATE BAG", StringComparison.OrdinalIgnoreCase);
        }

        private static bool StartsWithAddressKeyword(string value)
        {
            return value.StartsWith("UNIT ", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("FLAT ", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("SUITE ", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("SHOP ", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("STAND ", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("ERF ", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("PORTION ", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("CN:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNumberOnlyAddressPart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();

            return Regex.IsMatch(value, @"^\d+([\/\-]\d+)?$");
        }

        private static bool LooksLikeEmail(string value)
        {
            return value.Contains("@") && value.Contains(".");
        }

        private static bool HasText(string value)
        {
            return value.Any(char.IsLetter);
        }

        private static bool HasNumberAndText(string value)
        {
            return value.Any(char.IsDigit) && value.Any(char.IsLetter);
        }
    }

    public sealed class AddressLines
    {
        public string Addr1 { get; set; } = "";
        public string Addr2 { get; set; } = "";
        public string Addr3 { get; set; } = "";
        public string Addr4 { get; set; } = "";
        public string Addr5 { get; set; } = "";
    }
}