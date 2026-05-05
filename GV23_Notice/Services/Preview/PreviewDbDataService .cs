using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models;
using GV23_Notice.Models.DTOs;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace GV23_Notice.Services.Preview
{
    public sealed class PreviewDbDataService : IPreviewDbDataService
    {
        private readonly string _noticeDbConnStr;
        private readonly AppDbContext _db;


        public PreviewDbDataService(IConfiguration cfg, AppDbContext db)
        {
            _db = db;
            _noticeDbConnStr = cfg.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Missing DefaultConnection for Notice_DB.");
        }

        // -----------------------
        // PUBLIC METHODS
        // -----------------------
        private async Task<List<RowMap>> ExecListAsync(
    string proc,
    Action<SqlCommand> setup,
    CancellationToken ct)
        {
            var rows = new List<RowMap>();

            await using var cn = new SqlConnection(_noticeDbConnStr);
            await cn.OpenAsync(ct);

            await using var cmd = new SqlCommand(proc, cn)
            {
                CommandType = CommandType.StoredProcedure
            };

            setup(cmd);

            await using var rd = await cmd.ExecuteReaderAsync(ct);

            while (await rd.ReadAsync(ct))
            {
                rows.Add(RowMap.FromReader(rd));
            }

            return rows;
        }
        public async Task<S49PreviewDbData> S49PreviewDbDataAsync(int rollId, bool split, CancellationToken ct)
        {
            // 1) pick one row to discover premiseId
            var pickProc = split ? "dbo.S49_Preview_SelectSplitTop1" : "dbo.S49_Preview_SelectSingleTop1";

            var pickRow = await ExecSingleAsync(pickProc, cmd =>
            {
                cmd.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int) { Value = rollId });
            }, ct);

            if (pickRow is null)
                throw new InvalidOperationException("S49 preview: no roll row found.");

            var premiseId = pickRow.Str("PREMISEID") ?? pickRow.Str("PremiseId") ?? "";
            if (string.IsNullOrWhiteSpace(premiseId))
                throw new InvalidOperationException("S49 preview: missing PREMISEID.");

            // 2) now pull all rows for that premiseId
            var listProc = split
                ? "dbo.S49_Preview_SelectSplitByPremise"
                : "dbo.S49_Preview_SelectSingleByPremise"; // make this too, same idea but no split filter

            var rollRows = await ExecListAsync(listProc, cmd =>
            {
                cmd.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int) { Value = rollId });
                cmd.Parameters.Add(new SqlParameter("@PremiseId", SqlDbType.VarChar, 50) { Value = premiseId.Trim() });
            }, ct);

            if (rollRows.Count == 0)
                throw new InvalidOperationException("S49 preview: no roll rows found for premise.");

            // 3) sap contact
            var contactRow = await ExecSingleAsync("dbo.S49_Preview_SelectSapContactByPremise", cmd =>
            {
                cmd.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int) { Value = rollId });
                cmd.Parameters.Add(new SqlParameter("@PremiseId", SqlDbType.VarChar, 50) { Value = premiseId.Trim() });
            }, ct);

            // 4) pick a header row (newest)
            var header = rollRows[0];
            var s49Addr = BuildPreviewAddress(contactRow);

            return new S49PreviewDbData
            {
                RollId = rollId,
                PremiseId = premiseId,

                // Use header row for summary fields
                PropertyDesc = header.Str("PropertyDesc") ?? header.Str("Property_Desc"),
                LisStreetAddress = header.Str("LisStreetAddress"),
                ValuationKey = header.Str("VALUATIONKEY") ?? header.Str("ValuationKey"),
                CatDesc = header.Str("CatDesc"),
                RateableArea = header.Dec("RateableArea"),
                MarketValue = header.Dec("MarketValue"),
                Reason = header.Str("Reason"),
                ValuationSplitIndicator = header.Str("ValuationSplitIndicator"),

                Email = SafeEmail(contactRow?.Str("EMAIL_ADDR") ?? contactRow?.Str("Email")),
                
              
                Addr1 = s49Addr.Addr1,
                Addr2 = s49Addr.Addr2,
                Addr3 = s49Addr.Addr3,
                Addr4 = s49Addr.Addr4,
                Addr5 = s49Addr.Addr5,
                PremiseAddress = contactRow?.Str("PREMISE_ADDRESS"),
                AccountNo = contactRow?.Str("ACCOUNT_NO"),

                // ✅ THIS is what MapS49ToPdf must use
                RollRows = rollRows
            };
        }
        public async Task<S51PreviewDbData> S51PreviewDbDataAsync(
      int rollId,
      bool preferMulti,
      CancellationToken ct)
        {
            var procName = preferMulti
                ? "dbo.S51_Preview_SelectTop1_Multi"
                : "dbo.S51_Preview_SelectTop1_Single";

            var row = await ExecSingleAsync(procName, cmd =>
            {
                cmd.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int) { Value = rollId });
            }, ct);

            if (row is null)
                throw new InvalidOperationException($"S51 preview: no data found for {(preferMulti ? "multi" : "single")} mode.");
            var s51Addr = BuildPreviewAddress(row);
            return new S51PreviewDbData
            {
                RollId = rollId,
                ObjectionNo = row.Str("Objection_No") ?? row.Str("objection_No") ?? "",
                ObjectorType = row.Str("Objector_Type") ?? row.Str("objector_Type"),

                PremiseId = row.Str("Premise_iD") ?? row.Str("PremiseId"),
                PropertyDesc = row.Str("Property_desc") ?? row.Str("PropertyDesc"),
                Email = SafeEmail(row.Str("Email") ?? row.Str("EMAIL_ADDR")),
                valuationKey = row.Str("Valuation_Key"),
              

             
                Addr1 = s51Addr.Addr1,
                Addr2 = s51Addr.Addr2,
                Addr3 = s51Addr.Addr3,
                Addr4 = s51Addr.Addr4,
                Addr5 = s51Addr.Addr5,

                RandomPin = row.Str("RandomPin"),
                Section51Pin = row.Str("Section51Pin") ?? row.Str("RandomPin"),

                OldPropertyDescription = row.Str("Old_Property_Description"),
                OldAddress = row.Str("Old_Address"),
                OldOwner = row.Str("Old_Owner"),

                NewPropertyDescription = row.Str("New_Property_Description"),
                NewAddress = row.Str("New_Address"),
                NewOwner = row.Str("New_Owner"),

                OldCategory = row.Str("Old_Category"),
                Old2Category = row.Str("Old2_Category"),
                Old3Category = row.Str("Old3_Category"),

                OldExtent = row.Dec("Old_Extent"),
                Old2Extent = row.Dec("Old2_Extent"),
                Old3Extent = row.Dec("Old3_Extent"),

                OldMarketValue = row.Str("Old_Market_Value"),
                Old2MarketValue = row.Str("Old2_Market_Value"),
                Old3MarketValue = row.Str("Old3_Market_Value"),

                NewCategory = row.Str("New_Category"),
                New2Category = row.Str("New2_Category"),
                New3Category = row.Str("New3_Category"),

                NewExtent = row.Dec("New_Extent"),
                New2Extent = row.Dec("New2_Extent"),
                New3Extent = row.Dec("New3_Extent"),

                NewMarketValue = row.Str("New_Market_Value"),
                New2MarketValue = row.Str("New2_Market_Value"),
                New3MarketValue = row.Str("New3_Market_Value"),

                ObjectionReasons = row.Str("Objection_Reasons"),
                PropertyType = row.Str("Property_Type"),
            };
        }
        public async Task<S52PreviewDbData> S52PreviewDbDataAsync(
          int rollId,
          string appealNo,
          bool isReview,
          CancellationToken ct)
        {
            string proc;

            if (string.IsNullOrWhiteSpace(appealNo))
            {
                proc = isReview
                    ? "dbo.S52_Preview_SelectReviewByRange"
                    : "dbo.S52_Preview_SelectAppealByRange";
            }
            else
            {
                proc = isReview
                    ? "dbo.S52_Preview_SelectReviewTop1"
                    : "dbo.S52_Preview_SelectAppealTop1";
            }

            await using var conn = new SqlConnection(_noticeDbConnStr);
            await using var cmd = new SqlCommand(proc, conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            cmd.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int) { Value = rollId });

            if (string.IsNullOrWhiteSpace(appealNo))
            {
                cmd.Parameters.Add(new SqlParameter("@FromDate", SqlDbType.VarChar, 30) { Value = DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@ToDate", SqlDbType.VarChar, 30) { Value = DBNull.Value });
            }
            else
            {
                cmd.Parameters.Add(new SqlParameter("@AppealNo", SqlDbType.VarChar, 50)
                {
                    Value = appealNo.Trim()
                });
            }

            await conn.OpenAsync(ct);

            await using var reader = await cmd.ExecuteReaderAsync(ct);

            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException("S52 preview: no data found.");

            // =========================
            // SAFE ROW MAPPING
            // =========================
            var rowMap = Row.FromReader(reader);

            string Get(params string[] names)
            {
                foreach (var name in names)
                {
                    var val = rowMap.Str(name);
                    if (!string.IsNullOrWhiteSpace(val))
                        return val;
                }
                return "";
            }

            decimal? GetDec(params string[] names)
            {
                foreach (var name in names)
                {
                    var val = rowMap.Dec(name);
                    if (val.HasValue)
                        return val;
                }
                return null;
            }

            var s52Addr = BuildPreviewAddress(
                Get("ADDR1", "Addr1"),
                Get("ADDR2", "Addr2"),
                Get("ADDR3", "Addr3"),
                Get("ADDR4", "Addr4"),
                Get("ADDR5", "Addr5")
            );

            return new S52PreviewDbData
            {
                RollId = rollId,
                AppealNo = Get("Appeal_No"),
                ObjectionNo = Get("Objection_No"),

                AUserId = Get("A_UserID"),
                PremiseId = Get("Premise_iD", "PremiseId"),
                ValuationKey = Get("valuation_Key", "VALUATIONKEY"),
                PropertyDesc = Get("Property_desc", "PropertyDesc"),
                Email = SafeEmail(Get("Email")),

                Addr1 = s52Addr.Addr1,
                Addr2 = s52Addr.Addr2,
                Addr3 = s52Addr.Addr3,
                Addr4 = s52Addr.Addr4,
                Addr5 = s52Addr.Addr5,

                Town = Get("Town"),
                Erf = Get("ERF"),
                Ptn = Get("PTN"),
                Re = Get("RE"),

                AppMarketValue = Get("App_Market_Value"),
                AppMarketValue2 = Get("App_Market_Value2"),
                AppMarketValue3 = Get("App_Market_Value3"),

                AppExtent = GetDec("App_Extent"),
                AppExtent2 = GetDec("App_Extent2"),
                AppExtent3 = GetDec("App_Extent3"),

                AppCategory = Get("App_Category"),
                AppCategory2 = Get("App_Category2"),
                AppCategory3 = Get("App_Category3"),
            };
        }
        public async Task<S53PreviewDbData> S53PreviewDbDataAsync(int rollId, bool preferMulti, CancellationToken ct)
        {
            var procName = preferMulti
                ? "dbo.S53_Preview_SelectTop1_Multi"
                : "dbo.S53_Preview_SelectTop1_Single";

            var row = await ExecSingleAsync(procName, cmd =>
            {
                cmd.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int) { Value = rollId });
            }, ct);

            if (row is null)
                throw new InvalidOperationException($"S53 preview: no data found for {(preferMulti ? "multi" : "single")} mode.");
            var s53Addr = BuildPreviewAddress(row);
            return new S53PreviewDbData
            {
                RollId = rollId,
                ObjectionNo = row.Str("Objection_No"),
                PremiseId = row.Str("Premise_iD"),
                ValuationKey = row.Str("valuation_Key"),
                PropertyDesc = row.Str("Property_desc"),

                Email = SafeEmail(row.Str("Email")),

                

           
                Addr1 = s53Addr.Addr1,
                Addr2 = s53Addr.Addr2,
                Addr3 = s53Addr.Addr3,
                Addr4 = s53Addr.Addr4,
                Addr5 = s53Addr.Addr5,
              

                GvMarketValue = row.Str("GV_Market_Value"),
                GvMarketValue2 = row.Str("GV_Market_Value2"),
                GvMarketValue3 = row.Str("GV_Market_Value3"),

                GvExtent = row.Str("GV_Extent"),
                GvExtent2 = row.Str("GV_Extent2"),
                GvExtent3 = row.Str("GV_Extent3"),

                GvCategory = row.Str("GV_Category"),
                GvCategory2 = row.Str("GV_Category2"),
                GvCategory3 = row.Str("GV_Category3"),

                MvdMarketValue = row.Str("MVD_Market_Value"),
                MvdMarketValue2 = row.Str("MVD_Market_Value2"),
                MvdMarketValue3 = row.Str("MVD_Market_Value3"),

                MvdExtent = row.Str("MVD_Extent"),
                MvdExtent2 = row.Str("MVD_Extent2"),
                MvdExtent3 = row.Str("MVD_Extent3"),

                MvdCategory = row.Str("MVD_Category"),
                MvdCategory2 = row.Str("MVD_Category2"),
                MvdCategory3 = row.Str("MVD_Category3"),

                Section52Review = row.Str("Section52Review"),
                AppealCloseDate = row.Dt("Appeal_Close_Date")
            };
        }
        public async Task<DJPreviewDbData> DJPreviewDbDataAsync(int rollId, CancellationToken ct)
        {
            var row = await ExecSingleAsync("dbo.DJ_Preview_SelectPendingTop1", cmd =>
            {
                cmd.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int) { Value = rollId });
            }, ct);

            if (row is null)
                throw new InvalidOperationException("DJ preview: no pending Dear Johnny found.");

            var djAddr = BuildPreviewAddress(row);
            return new DJPreviewDbData
            {
                RollId = rollId,
                ObjectionNo = row.Str("Objection_No") ?? "",
                PremiseId = row.Str("Premise_iD"),
                PropertyDesc = row.Str("Property_desc"),
                Email = SafeEmail(row.Str("Email")),
                ValuationKey = row.Str("valuation_Key") ?? row.Str("VALUATIONKEY"),


            

                Addr1 = djAddr.Addr1,
                Addr2 = djAddr.Addr2,
                Addr3 = djAddr.Addr3,
                Addr4 = djAddr.Addr4,
                Addr5 = djAddr.Addr5,

               
            };
        }

        public async Task<InvalidPreviewDbData> InvalidPreviewDbDataAsync(int rollId, bool isOmission, CancellationToken ct)
        {
            var primaryProc = isOmission
                ? "dbo.IN_Preview_SelectInvalidOmissionTop1"
                : "dbo.IN_Preview_SelectInvalidObjectionTop1";

            var fallbackProc = isOmission
                ? "dbo.IN_Preview_SelectInvalidObjectionTop1"
                : "dbo.IN_Preview_SelectInvalidOmissionTop1";

            var row = await ExecSingleAsync(primaryProc, cmd =>
            {
                cmd.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int) { Value = rollId });
            }, ct);

            if (row is null)
            {
                row = await ExecSingleAsync(fallbackProc, cmd =>
                {
                    cmd.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int) { Value = rollId });
                }, ct);
            }

            if (row is null)
            {
                return new InvalidPreviewDbData
                {
                    RollId = rollId,
                    ObjectionNo = "PREVIEW-INVALID-001",
                    PremiseId = "",
                    PropertyDesc = "INVALID PREVIEW PROPERTY",
                    Email = "",
                    ValuationKey = "VAL-KEY-PREVIEW",
                    Addr1 = "XXXX",
                    Addr2 = "XXXX",
                    Addr3 = "XXXX",
                    Addr4 = "XXXX",
                    Addr5 = "",
                    ObjectionStatus = isOmission ? "Pending-Invalid Omission" : "Pending-Invalid Objection"
                };
            }

            var addr1 = row.Str("ADDR1");
            var addr2 = row.Str("ADDR2");
            var addr3 = row.Str("ADDR3");
            var addr4 = row.Str("ADDR4");
            var addr5 = row.Str("ADDR5");

            var hasAnyAddress =
                !string.IsNullOrWhiteSpace(addr1) ||
                !string.IsNullOrWhiteSpace(addr2) ||
                !string.IsNullOrWhiteSpace(addr3) ||
                !string.IsNullOrWhiteSpace(addr4) ||
                !string.IsNullOrWhiteSpace(addr5);

            return new InvalidPreviewDbData
            {
                RollId = rollId,
                ObjectionNo = row.Str("Objection_No") ?? "PREVIEW-INVALID-001",
                PremiseId = row.Str("Premise_iD"),
                PropertyDesc = row.Str("Property_desc") ?? row.Str("Property_Desc") ?? "INVALID PREVIEW PROPERTY",
                Email = SafeEmail(row.Str("Email")),
                ValuationKey = row.Str("valuation_Key") ?? row.Str("Valuation_Key") ?? row.Str("VALUATIONKEY") ?? "VAL-KEY-PREVIEW",

                Addr1 = hasAnyAddress ? (addr1 ?? "") : "XXXX",
                Addr2 = hasAnyAddress ? (addr2 ?? "") : "XXXX",
                Addr3 = hasAnyAddress ? (addr3 ?? "") : "XXXX",
                Addr4 = hasAnyAddress ? (addr4 ?? "") : "XXXX",
                Addr5 = hasAnyAddress ? (addr5 ?? "") : "",

                ObjectionStatus = row.Str("Objection_Status") ?? ""
            };
        }
        // -----------------------
        // INTERNAL HELPERS
        // -----------------------

        private async Task<Row?> ExecSingleAsync(string storedProc, Action<SqlCommand> bind, CancellationToken ct)
        {
            await using var cn = new SqlConnection(_noticeDbConnStr);
            await cn.OpenAsync(ct);

            await using var cmd = new SqlCommand(storedProc, cn)
            {
                CommandType = CommandType.StoredProcedure
            };

            bind(cmd);

            await using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult, ct);
            if (!await rd.ReadAsync(ct))
                return null;

            return Row.FromReader(rd);
        }

        private sealed class Row
        {
            private readonly Dictionary<string, object?> _map;

            private Row(Dictionary<string, object?> map) => _map = map;

            public static Row FromReader(SqlDataReader rd)
            {
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < rd.FieldCount; i++)
                {
                    var name = rd.GetName(i);
                    var val = rd.IsDBNull(i) ? null : rd.GetValue(i);
                    dict[name] = val;
                }
                return new Row(dict);
            }

            public string? Str(string name)
                => _map.TryGetValue(name, out var v) ? v?.ToString() : null;

            public decimal? Dec(string name)
            {
                if (!_map.TryGetValue(name, out var v) || v is null) return null;
                if (v is decimal d) return d;
                if (decimal.TryParse(v.ToString(), out var parsed)) return parsed;
                return null;
            }

            public DateTime? Dt(string name)
            {
                if (!_map.TryGetValue(name, out var v) || v is null) return null;
                if (v is DateTime dt) return dt;
                if (DateTime.TryParse(v.ToString(), out var parsed)) return parsed;
                return null;
            }
        }


        public async Task<NoticePreviewSnapshot> GetSnapshotForRunLogAsync(int runLogId, CancellationToken ct)
        {
            var snap = await _db.NoticePreviewSnapshots
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.NoticeRunLogId == runLogId, ct);

            if (snap is null)
                throw new InvalidOperationException($"Snapshot not found for RunLogId={runLogId}. (Create snapshots in Step3-Step2 first)");

            return snap;
        }

        public async Task<NoticePreviewSnapshot?> TryGetSnapshotForRunLogAsync(int runLogId, CancellationToken ct)
        {
            return await _db.NoticePreviewSnapshots
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.NoticeRunLogId == runLogId, ct);
        }

        public async Task<NoticePreviewSnapshot> S49ByPremiseIdAsync(int settingsId, string premiseId, CancellationToken ct)
      => await FindByKeyAsync(settingsId, NoticeKind.S49, premiseId: premiseId, ct: ct);

        public async Task<NoticePreviewSnapshot> S51ByObjectionNoAsync(int settingsId, string objectionNo, CancellationToken ct)
            => await FindByKeyAsync(settingsId, NoticeKind.S51, objectionNo: objectionNo, ct: ct);

        public async Task<NoticePreviewSnapshot> S53ByObjectionNoAsync(int settingsId, string objectionNo, CancellationToken ct)
            => await FindByKeyAsync(settingsId, NoticeKind.S53, objectionNo: objectionNo, ct: ct);

        public async Task<NoticePreviewSnapshot> DJByObjectionNoAsync(int settingsId, string objectionNo, CancellationToken ct)
            => await FindByKeyAsync(settingsId, NoticeKind.DJ, objectionNo: objectionNo, ct: ct);
        public async Task<NoticePreviewSnapshot> S52ByAppealNoAsync(int settingsId, string appealNo, bool isReview, CancellationToken ct)
        {
            // Variant must distinguish review vs appeal-decision
            var variant = isReview ? "S52Review" : "AppealDecision";
            var snap = await _db.NoticePreviewSnapshots
                .AsNoTracking()
                .Where(x => x.SettingsId == settingsId
                         && x.Notice == NoticeKind.S52
                         && x.Variant == variant
                         && x.AppealNo == appealNo)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            if (snap is null)
                throw new InvalidOperationException($"Snapshot not found for SettingsId={settingsId}, AppealNo={appealNo}, Variant={variant}.");

            return snap;
        }


        public async Task<NoticePreviewSnapshot> InvalidByObjectionNoAsync(int settingsId, string objectionNo, bool isOmission, CancellationToken ct)
        {
            var variant = isOmission ? "InvalidOmission" : "InvalidObjection";
            var snap = await _db.NoticePreviewSnapshots
                .AsNoTracking()
                .Where(x => x.SettingsId == settingsId
                         && x.Notice == NoticeKind.IN
                         && x.Variant == variant
                         && x.ObjectionNo == objectionNo)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            if (snap is null)
                throw new InvalidOperationException($"Snapshot not found for SettingsId={settingsId}, ObjectionNo={objectionNo}, Variant={variant}.");

            return snap;
        }

        private async Task<NoticePreviewSnapshot> FindByKeyAsync(
            int settingsId,
            NoticeKind notice,
            string? objectionNo = null,
            string? appealNo = null,
            string? premiseId = null,
            CancellationToken ct = default)
        {
            var q = _db.NoticePreviewSnapshots.AsNoTracking()
                .Where(x => x.SettingsId == settingsId && x.Notice == notice);

            if (!string.IsNullOrWhiteSpace(objectionNo))
                q = q.Where(x => x.ObjectionNo == objectionNo);

            if (!string.IsNullOrWhiteSpace(appealNo))
                q = q.Where(x => x.AppealNo == appealNo);

            if (!string.IsNullOrWhiteSpace(premiseId))
                q = q.Where(x => x.PremiseId == premiseId);

            var snap = await q.OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);

            if (snap is null)
                throw new InvalidOperationException($"Snapshot not found for SettingsId={settingsId}, Notice={notice}, key(s).");

            return snap;
        }
        private static (string Addr1, string Addr2, string Addr3, string Addr4, string Addr5) BuildPreviewAddress(
      string? addr1,
      string? addr2,
      string? addr3,
      string? addr4,
      string? addr5)
        {
            var a1 = addr1?.Trim() ?? "";
            var a2 = addr2?.Trim() ?? "";
            var a3 = addr3?.Trim() ?? "";
            var a4 = addr4?.Trim() ?? "";
            var a5 = addr5?.Trim() ?? "";

            var hasAnyAddress =
                !string.IsNullOrWhiteSpace(a1) ||
                !string.IsNullOrWhiteSpace(a2) ||
                !string.IsNullOrWhiteSpace(a3) ||
                !string.IsNullOrWhiteSpace(a4) ||
                !string.IsNullOrWhiteSpace(a5);

            if (!hasAnyAddress)
            {
                return ("XXXX", "XXXX", "XXXX", "XXXX", "");
            }

            return (a1, a2, a3, a4, a5);
        }

        private static (string Addr1, string Addr2, string Addr3, string Addr4, string Addr5) BuildPreviewAddress(Row? row)
        {
            return BuildPreviewAddress(
                row?.Str("ADDR1"),
                row?.Str("ADDR2"),
                row?.Str("ADDR3"),
                row?.Str("ADDR4"),
                row?.Str("ADDR5"));
        }
        private static string SafeEmail(string? value)
    => string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
    }
}