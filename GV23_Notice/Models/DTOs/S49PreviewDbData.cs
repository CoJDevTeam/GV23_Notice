using System.Data;
using System.Globalization;

namespace GV23_Notice.Models.DTOs
{
    public sealed class S49PreviewDbData
    {
        public int RollId { get; set; }

        // roll row
        public string PremiseId { get; set; } = "";
        public string? PropertyDesc { get; set; }
        public string? LisStreetAddress { get; set; }
        public string? ValuationKey { get; set; }   // VALUATIONKEY
        public string? CatDesc { get; set; }
        public decimal? RateableArea { get; set; }
        public decimal? MarketValue { get; set; }
        public string? Reason { get; set; }
        public string? ValuationSplitIndicator { get; set; }

        // contact
        public string? Email { get; set; }
        public string? Addr1 { get; set; }
        public string? Addr2 { get; set; }
        public string? Addr3 { get; set; }
        public string? Addr4 { get; set; }
        public string? Addr5 { get; set; }
        public string? PremiseAddress { get; set; }
        public string? AccountNo { get; set; }
        public List<RowMap> RollRows { get; set; } = new();

    }

    public sealed class S51PreviewDbData
    {
        public int RollId { get; set; }
        public string ObjectionNo { get; set; } = "";
        public string? ObjectorType { get; set; }

        // Obj_Property_Info
        public string? PremiseId { get; set; }
        public string? PropertyDesc { get; set; }
        public string? valuationKey { get; set; }
        public string? Email { get; set; }
        public string? Addr1 { get; set; }
        public string? Addr2 { get; set; }
        public string? Addr3 { get; set; }
        public string? Addr4 { get; set; }
        public string? Addr5 { get; set; }

        // Obj_Section7
        public string? RandomPin { get; set; }
        public string? Section51Pin { get; set; }

        // Obj_Section6 - descriptive fields
        public string? OldPropertyDescription { get; set; }
        public string? OldAddress { get; set; }
        public string? OldOwner { get; set; }

        public string? NewPropertyDescription { get; set; }
        public string? NewAddress { get; set; }
        public string? NewOwner { get; set; }

        // Obj_Section6 - main comparison fields
        public string? OldCategory { get; set; }
        public string? Old2Category { get; set; }
        public string? Old3Category { get; set; }

        public decimal? OldExtent { get; set; }
        public decimal? Old2Extent { get; set; }
        public decimal? Old3Extent { get; set; }

        public string? OldMarketValue { get; set; }
        public string? Old2MarketValue { get; set; }
        public string? Old3MarketValue { get; set; }

        public string? NewCategory { get; set; }
        public string? New2Category { get; set; }
        public string? New3Category { get; set; }

        public decimal? NewExtent { get; set; }
        public decimal? New2Extent { get; set; }
        public decimal? New3Extent { get; set; }

        public string? NewMarketValue { get; set; }
        public string? New2MarketValue { get; set; }
        public string? New3MarketValue { get; set; }

        public string? ObjectionReasons { get; set; }
        public string? PropertyType { get; set; }
        public bool IsMulti =>
            string.Equals(PropertyType?.Trim(), "Multi", StringComparison.OrdinalIgnoreCase);

    }

    public sealed class S52PreviewDbData
    {
        public int RollId { get; set; }
        public string AppealNo { get; set; } = "";
        public string? ObjectionNo { get; set; }

        public string? AUserId { get; set; } // System_Generated vs user
        public string? PremiseId { get; set; }
        public string? ValuationKey { get; set; }
        public string? PropertyDesc { get; set; }
        public string? Email { get; set; }

        public string? Addr1 { get; set; }
        public string? Addr2 { get; set; }
        public string? Addr3 { get; set; }
        public string? Addr4 { get; set; }
        public string? Addr5 { get; set; }

        public string? Town { get; set; }
        public string? Erf { get; set; }
        public string? Ptn { get; set; }
        public string? Re { get; set; }

        public string? AppMarketValue { get; set; }
        public string? AppMarketValue2 { get; set; }
        public string? AppMarketValue3 { get; set; }

        public decimal? AppExtent { get; set; }
        public decimal? AppExtent2 { get; set; }
        public decimal? AppExtent3 { get; set; }

        public string? AppCategory { get; set; }
        public string? AppCategory2 { get; set; }
        public string? AppCategory3 { get; set; }
    }

    public sealed class S53PreviewDbData
    {
        public int RollId { get; set; }
        public string ObjectionNo { get; set; } = "";

        public string? PremiseId { get; set; }
        public string? ValuationKey { get; set; }
        public string? PropertyDesc { get; set; }
        public string? Email { get; set; }

        public string? Addr1 { get; set; }
        public string? Addr2 { get; set; }
        public string? Addr3 { get; set; }
        public string? Addr4 { get; set; }
        public string? Addr5 { get; set; }

        // GV (roll values)
        public string? GvMarketValue { get; set; }
        public string? GvMarketValue2 { get; set; }
        public string? GvMarketValue3 { get; set; }

        public string? GvExtent { get; set; }
        public string? GvExtent2 { get; set; }
        public string? GvExtent3 { get; set; }

        public string? GvCategory { get; set; }
        public string? GvCategory2 { get; set; }
        public string? GvCategory3 { get; set; }

        // MVD decision values
        public string? MvdMarketValue { get; set; }
        public string? MvdMarketValue2 { get; set; }
        public string? MvdMarketValue3 { get; set; }

        public string? MvdExtent { get; set; }
        public string? MvdExtent2 { get; set; }
        public string? MvdExtent3 { get; set; }

        public string? MvdCategory { get; set; }
        public string? MvdCategory2 { get; set; }
        public string? MvdCategory3 { get; set; }

        public string? Section52Review { get; set; }
        public DateTime? AppealCloseDate { get; set; }
        public DateTime? BatchDate { get; set; }
        public string? BatchName { get; set; }
    }

    public sealed class DJPreviewDbData
    {
        public int RollId { get; set; }
        public string ObjectionNo { get; set; } = "";
        public string? PremiseId { get; set; }
        public string? PropertyDesc { get; set; }

        public string? Email { get; set; }
        public string? Addr1 { get; set; }
        public string? Addr2 { get; set; }
        public string? Addr3 { get; set; }
        public string? Addr4 { get; set; }
        public string? Addr5 { get; set; }
    }

    public sealed class InvalidPreviewDbData
    {
        public int RollId { get; set; }
        public string ObjectionNo { get; set; } = "";
        public string? PremiseId { get; set; }
        public string? PropertyDesc { get; set; }

        public string? Email { get; set; }
        public string? Addr1 { get; set; }
        public string? Addr2 { get; set; }
        public string? Addr3 { get; set; }
        public string? Addr4 { get; set; }
        public string? Addr5 { get; set; }

        public string? ObjectionStatus { get; set; } // Invalid-Objection / Invalid-Omission
    }

    public sealed class RowMap
    {
        private readonly Dictionary<string, object?> _data;

        private RowMap(Dictionary<string, object?> data)
        {
            _data = data;
        }

        // -----------------------------
        // Factory
        // -----------------------------

        public static RowMap FromReader(IDataRecord rd)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < rd.FieldCount; i++)
            {
                dict[rd.GetName(i)] = rd.IsDBNull(i) ? null : rd.GetValue(i);
            }

            return new RowMap(dict);
        }

        // -----------------------------
        // Core access
        // -----------------------------

        public object? this[string key]
            => _data.TryGetValue(key, out var v) ? v : null;

        public bool Has(string key)
            => _data.ContainsKey(key);

        // -----------------------------
        // Typed helpers (safe)
        // -----------------------------

        public string? Str(string key)
        {
            if (!_data.TryGetValue(key, out var v) || v is null)
                return null;

            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public int? Int(string key)
        {
            if (!_data.TryGetValue(key, out var v) || v is null)
                return null;

            if (v is int i) return i;

            return int.TryParse(v.ToString(), out var result)
                ? result
                : null;
        }

        public decimal? Dec(string key)
        {
            if (!_data.TryGetValue(key, out var v) || v is null)
                return null;

            if (v is decimal d) return d;

            return decimal.TryParse(
                v.ToString(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var result)
                ? result
                : null;
        }

        public bool? Bool(string key)
        {
            if (!_data.TryGetValue(key, out var v) || v is null)
                return null;

            if (v is bool b) return b;

            var s = v.ToString()?.Trim().ToLowerInvariant();

            if (s is "1" or "y" or "yes" or "true") return true;
            if (s is "0" or "n" or "no" or "false") return false;

            return null;
        }

        public DateTime? Date(string key)
        {
            if (!_data.TryGetValue(key, out var v) || v is null)
                return null;

            if (v is DateTime dt) return dt;

            return DateTime.TryParse(v.ToString(), out var result)
                ? result
                : null;
        }

        // -----------------------------
        // Debug / diagnostics
        // -----------------------------

        public IReadOnlyDictionary<string, object?> Raw => _data;
    }
}
