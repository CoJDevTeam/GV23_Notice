using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Models.Corrections.ViewModels;
using GV23_Notice.Models.Workflow.ViewModels;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;

namespace GV23_Notice.Services.Corrections
{
    public sealed class NoticeCorrectionSourceService : INoticeCorrectionSourceService
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;

        public NoticeCorrectionSourceService(AppDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        public async Task<CorrectionPreviewVm?> SearchAsync(
    int rollId,
    NoticeKind sourceNotice,
    NoticeKind printNotice,
    string referenceType,
    string referenceNo,
    CancellationToken ct)
        {
            referenceType = (referenceType ?? "").Trim();
            referenceNo = (referenceNo ?? "").Trim();

            if (string.IsNullOrWhiteSpace(referenceType))
                throw new InvalidOperationException("Reference type is required.");

            if (string.IsNullOrWhiteSpace(referenceNo))
                throw new InvalidOperationException("Reference number is required.");

            var roll = await _db.RollRegistry.AsNoTracking()
                .FirstOrDefaultAsync(x => x.RollId == rollId, ct)
                ?? throw new InvalidOperationException($"Roll {rollId} was not found.");

            if (string.IsNullOrWhiteSpace(roll.SourceDb))
                throw new InvalidOperationException($"Roll {roll.ShortCode} has no SourceDb configured.");

            return sourceNotice switch
            {
                NoticeKind.S53 => await SearchS53Async(
                    roll.RollId,
                    roll.ShortCode ?? "",
                    roll.Name ?? "",
                    roll.SourceDb,
                    sourceNotice,
                    printNotice,
                    referenceType,
                    referenceNo,
                    false,
                    ct),

                NoticeKind.S53Rev => await SearchS53Async(
                    roll.RollId,
                    roll.ShortCode ?? "",
                    roll.Name ?? "",
                    roll.SourceDb,
                    sourceNotice,
                    printNotice,
                    referenceType,
                    referenceNo,
                    true,
                    ct),

                _ => throw new InvalidOperationException($"Correction search for {sourceNotice} is not added yet. We are starting with Section 53 and Revised MVD first.")
            };
        }

        private async Task<CorrectionPreviewVm?> SearchS53Async(
     int rollId,
     string rollShortCode,
     string rollName,
     string sourceDb,
     NoticeKind sourceNotice,
     NoticeKind printNotice,
     string referenceType,
     string referenceNo,
     bool isRevisedSource,
     CancellationToken ct)
        {
            var baseConn = _config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection is missing.");

            var sql = $@"
SELECT
    m.Objection_No,
    m.Objector_Type,
    CAST(m.Premise_iD AS NVARCHAR(100)) AS PremiseId,
    CAST(m.Unit_Key AS NVARCHAR(100)) AS UnitKey,
    CAST(m.valuation_Key AS NVARCHAR(100)) AS ValuationKey,
    m.Property_desc,
    m.Email,
    m.ADDR1,
    m.ADDR2,
    m.ADDR3,
    m.ADDR4,
    m.ADDR5,

    m.GV_Category,
    m.GV_Category2,
    m.GV_Category3,
    m.GV_Market_Value,
    m.GV_Market_Value2,
    m.GV_Market_Value3,
    m.GV_Extent,
    m.GV_EXtent2,
    m.GV_Extent3,

    m.MVD_Category,
    m.MVD_Category2,
    m.MVD_Category3,
    m.MVD_Market_Value,
    m.MVD_Market_Value2,
    m.MVD_Market_Value3,
    m.MVD_Extent,
    m.MVD_Extent2,
    m.MVD_Extent3,

    m.Revise_MVD,
    m.ReviseMVD_Category,
    m.ReviseMVD_Category2,
    m.ReviseMVD_Category3,
    m.ReviseMVD_Market_Value,
    m.ReviseMVD_Market_Value2,
    m.ReviseMVD_Market_Value3,
    m.ReviseMVD_Extent,
    m.ReviseMVD_Extent2,
    m.ReviseMVD_Extent3,

    m.Section52Review,
    m.Section52Review_Revise_MVD,

    m.Batch_Date,
    m.Appeal_Start_Date,
    m.Appeal_Close_Date,

    m.Batch_Date_ReviseMVD,
    m.Appeal_Start_Date_ReviseMVD,
    m.Appeal_Close_Date_ReviseMVD,

    m.wefDateMVD
FROM [{sourceDb}].dbo.Objection_MVD m
WHERE
(
    (@ReferenceType = 'Objection_No' AND LTRIM(RTRIM(ISNULL(m.Objection_No, ''))) = @ReferenceNo)
    OR
    (@ReferenceType = 'PremiseId' AND LTRIM(RTRIM(ISNULL(CAST(m.Premise_iD AS NVARCHAR(100)), ''))) = @ReferenceNo)
    OR
    (@ReferenceType = 'ValuationKey' AND LTRIM(RTRIM(ISNULL(CAST(m.valuation_Key AS NVARCHAR(100)), ''))) = @ReferenceNo)
)
AND LTRIM(RTRIM(ISNULL(m.Objector_Type, ''))) IN
(
    'Owner',
    'Representative',
    'Owner_Rep',
    'Third_Party',
    'Owner_Third_Party'
)
ORDER BY
    CASE LTRIM(RTRIM(ISNULL(m.Objector_Type, '')))
        WHEN 'Owner' THEN 1
        WHEN 'Representative' THEN 2
        WHEN 'Owner_Rep' THEN 3
        WHEN 'Third_Party' THEN 4
        WHEN 'Owner_Third_Party' THEN 5
        ELSE 99
    END,
    m.Objection_No;
";

            await using var conn = new SqlConnection(baseConn);
            await using var cmd = new SqlCommand(sql, conn)
            {
                CommandType = CommandType.Text,
                CommandTimeout = 90
            };

            cmd.Parameters.Add(new SqlParameter("@ReferenceType", SqlDbType.NVarChar, 50) { Value = referenceType });
            cmd.Parameters.Add(new SqlParameter("@ReferenceNo", SqlDbType.NVarChar, 100) { Value = referenceNo });

            await conn.OpenAsync(ct);
            await using var rd = await cmd.ExecuteReaderAsync(ct);

            var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < rd.FieldCount; i++)
                colMap[rd.GetName(i)] = i;

            string? Str(string col)
            {
                if (!colMap.TryGetValue(col, out var o) || rd.IsDBNull(o))
                    return null;

                return rd.GetValue(o)?.ToString();
            }

            DateTime? Dt(string col)
            {
                if (!colMap.TryGetValue(col, out var o) || rd.IsDBNull(o))
                    return null;

                var v = rd.GetValue(o);
                if (v is DateTime dt) return dt;

                return DateTime.TryParse(v.ToString(), out var parsed) ? parsed : null;
            }

            var items = new List<CorrectionPreviewItemVm>();

            while (await rd.ReadAsync(ct))
            {
                var item = new CorrectionPreviewItemVm
                {
                    ObjectionNo = Str("Objection_No"),

                    PremiseId = Str("PremiseId"),
                    UnitKey = Str("UnitKey"),
                    ValuationKey = Str("ValuationKey"),

                    PropertyDesc = Str("Property_desc"),

                    RecipientRole = Str("Objector_Type"),
                    RecipientEmail = Str("Email"),

                    ADDR1 = Str("ADDR1"),
                    ADDR2 = Str("ADDR2"),
                    ADDR3 = Str("ADDR3"),
                    ADDR4 = Str("ADDR4"),
                    ADDR5 = Str("ADDR5"),

                    OldCategory = Str("GV_Category"),
                    OldCategory2 = Str("GV_Category2"),
                    OldCategory3 = Str("GV_Category3"),

                    OldMarketValue = Str("GV_Market_Value"),
                    OldMarketValue2 = Str("GV_Market_Value2"),
                    OldMarketValue3 = Str("GV_Market_Value3"),

                    OldExtent = Str("GV_Extent"),
                    OldExtent2 = Str("GV_EXtent2"),
                    OldExtent3 = Str("GV_Extent3"),

                    NewCategory = isRevisedSource ? Str("ReviseMVD_Category") : Str("MVD_Category"),
                    NewCategory2 = isRevisedSource ? Str("ReviseMVD_Category2") : Str("MVD_Category2"),
                    NewCategory3 = isRevisedSource ? Str("ReviseMVD_Category3") : Str("MVD_Category3"),

                    NewMarketValue = isRevisedSource ? Str("ReviseMVD_Market_Value") : Str("MVD_Market_Value"),
                    NewMarketValue2 = isRevisedSource ? Str("ReviseMVD_Market_Value2") : Str("MVD_Market_Value2"),
                    NewMarketValue3 = isRevisedSource ? Str("ReviseMVD_Market_Value3") : Str("MVD_Market_Value3"),

                    NewExtent = isRevisedSource ? Str("ReviseMVD_Extent") : Str("MVD_Extent"),
                    NewExtent2 = isRevisedSource ? Str("ReviseMVD_Extent2") : Str("MVD_Extent2"),
                    NewExtent3 = isRevisedSource ? Str("ReviseMVD_Extent3") : Str("MVD_Extent3"),

                    WEFDate = Str("wefDateMVD"),

                    BatchDate = isRevisedSource ? Dt("Batch_Date_ReviseMVD") : Dt("Batch_Date"),
                    AppealStartDate = isRevisedSource ? Dt("Appeal_Start_Date_ReviseMVD") : Dt("Appeal_Start_Date"),
                    AppealCloseDate = isRevisedSource ? Dt("Appeal_Close_Date_ReviseMVD") : Dt("Appeal_Close_Date"),

                    Section52Review = isRevisedSource ? Str("Section52Review_Revise_MVD") : Str("Section52Review")
                };

                item.Fields = BuildCommonFields(item);
                item.SnapshotJson = System.Text.Json.JsonSerializer.Serialize(item);

                items.Add(item);
            }

            if (items.Count == 0)
                return null;

            return new CorrectionPreviewVm
            {
                RollId = rollId,
                RollShortCode = rollShortCode,
                RollName = rollName,
                SourceDb = sourceDb,

                SourceNotice = sourceNotice,
                PrintNotice = printNotice,

                SourceNoticeText = NoticeDisplayName(sourceNotice),
                PrintNoticeText = NoticeDisplayName(printNotice),

                ReferenceType = referenceType,
                ReferenceNo = referenceNo,

                NoticeSubKind = isRevisedSource ? "RevisedMVD" : "MVD",

                Items = items
            };
        }

        private static List<CorrectionFieldRowVm> BuildCommonFields(CorrectionPreviewItemVm vm)
        {
            var rows = new List<CorrectionFieldRowVm>();

            void Add(string section, string field, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    rows.Add(new CorrectionFieldRowVm
                    {
                        Section = section,
                        Field = field,
                        Value = value
                    });
                }
            }

            Add("Reference", "Objection No", vm.ObjectionNo);
            Add("Property", "Premise ID", vm.PremiseId);
            Add("Property", "Valuation Key", vm.ValuationKey);
            Add("Property", "Unit Key", vm.UnitKey);
            Add("Property", "Property Description", vm.PropertyDesc);

            Add("Recipient", "Objector Type", vm.RecipientRole);
            Add("Recipient", "Recipient Email", vm.RecipientEmail);
            Add("Recipient", "ADDR1", vm.ADDR1);
            Add("Recipient", "ADDR2", vm.ADDR2);
            Add("Recipient", "ADDR3", vm.ADDR3);
            Add("Recipient", "ADDR4", vm.ADDR4);
            Add("Recipient", "ADDR5", vm.ADDR5);

            Add("GV Entry", "Category", vm.OldCategory);
            Add("GV Entry", "Market Value", vm.OldMarketValue);
            Add("GV Entry", "Extent", vm.OldExtent);

            Add("GV Entry Split 1", "Category", vm.OldCategory2);
            Add("GV Entry Split 1", "Market Value", vm.OldMarketValue2);
            Add("GV Entry Split 1", "Extent", vm.OldExtent2);

            Add("GV Entry Split 2", "Category", vm.OldCategory3);
            Add("GV Entry Split 2", "Market Value", vm.OldMarketValue3);
            Add("GV Entry Split 2", "Extent", vm.OldExtent3);

            Add("Corrected Decision", "Category", vm.NewCategory);
            Add("Corrected Decision", "Market Value", vm.NewMarketValue);
            Add("Corrected Decision", "Extent", vm.NewExtent);

            Add("Corrected Decision Split 1", "Category", vm.NewCategory2);
            Add("Corrected Decision Split 1", "Market Value", vm.NewMarketValue2);
            Add("Corrected Decision Split 1", "Extent", vm.NewExtent2);

            Add("Corrected Decision Split 2", "Category", vm.NewCategory3);
            Add("Corrected Decision Split 2", "Market Value", vm.NewMarketValue3);
            Add("Corrected Decision Split 2", "Extent", vm.NewExtent3);

            Add("Dates", "WEF Date", vm.WEFDate);
            Add("Dates", "Appeal Close Date", vm.AppealCloseDate?.ToString("d MMMM yyyy"));
            Add("Dates", "Section 52 Review", vm.Section52Review);

            return rows;
        }

        private static string NoticeDisplayName(NoticeKind notice)
        {
            return notice switch
            {
                NoticeKind.S49 => "Section 49",
                NoticeKind.S51 => "Section 51",
                NoticeKind.S52 => "Section 52",
                NoticeKind.S53 => "Section 53 MVD",
                NoticeKind.S53Rev => "Section 53 Revised MVD",
                NoticeKind.DJ => "Dear Johnny",
                NoticeKind.IN => "Invalid Notice",
                NoticeKind.S78 => "Section 78",
                _ => notice.ToString()
            };
        }
    }
}