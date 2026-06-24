// Helpers/ExtentDisplayHelper.cs

using System.Globalization;

namespace GV23_Notice.Helpers
{
    public static class ExtentDisplayHelper
    {
        /// <summary>
        /// Keeps extent as close as possible to the DB value.
        /// - string "10,58" stays "10,58"
        /// - string "10.58" stays "10.58"
        /// - decimal 10.58 stays "10.58"
        /// - decimal 1000.00 becomes "1000"
        /// No thousands spaces. No forced decimals.
        /// </summary>
        public static string SameAsDb(object? value)
        {
            if (value is null || value == DBNull.Value)
                return "";

            if (value is string s)
                return s.Trim();

            if (value is decimal dec)
                return dec.ToString("0.############################", CultureInfo.InvariantCulture);

            if (value is double dbl)
                return dbl.ToString("0.############################", CultureInfo.InvariantCulture);

            if (value is float flt)
                return flt.ToString("0.############################", CultureInfo.InvariantCulture);

            return Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
        }
    }
}