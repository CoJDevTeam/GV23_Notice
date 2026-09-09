using System.Globalization;

namespace GV23_Notice.Helpers
{
    public static class ExtentDisplayHelper
    {
        /// <summary>
        /// Formats extent/rateable area for notices without forced trailing zeros.
        ///
        /// Examples:
        /// 2330.0000   -> 2330
        /// 10229.0000  -> 10229
        /// 17835.5000  -> 17835.5
        /// 17835.2500  -> 17835.25
        ///
        /// This deliberately avoids N2/N4 formatting and preserves only
        /// meaningful decimal digits.
        /// </summary>
        public static string SameAsDb(object? value)
        {
            if (value is null || value == DBNull.Value)
                return "";

            if (value is decimal dec)
            {
                return dec.ToString(
                    "0.############################",
                    CultureInfo.InvariantCulture);
            }

            if (value is double dbl)
            {
                return dbl.ToString(
                    "0.############################",
                    CultureInfo.InvariantCulture);
            }

            if (value is float flt)
            {
                return flt.ToString(
                    "0.############################",
                    CultureInfo.InvariantCulture);
            }

            if (value is string s)
            {
                var text = s.Trim();

                if (string.IsNullOrWhiteSpace(text))
                    return "";

                // SQL decimal values may arrive as strings such as
                // "2330.0000" or "2330,0000". Parse and remove only
                // insignificant trailing zeros.
                if (decimal.TryParse(
                        text,
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out var invariantValue))
                {
                    return invariantValue.ToString(
                        "0.############################",
                        CultureInfo.InvariantCulture);
                }

                var za =
                    CultureInfo.GetCultureInfo("en-ZA");

                if (decimal.TryParse(
                        text,
                        NumberStyles.Any,
                        za,
                        out var zaValue))
                {
                    return zaValue.ToString(
                        "0.############################",
                        CultureInfo.InvariantCulture);
                }

                return text;
            }

            var converted =
                Convert.ToString(
                    value,
                    CultureInfo.InvariantCulture);

            if (decimal.TryParse(
                    converted,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                return parsed.ToString(
                    "0.############################",
                    CultureInfo.InvariantCulture);
            }

            return converted?.Trim() ?? "";
        }
    }
}