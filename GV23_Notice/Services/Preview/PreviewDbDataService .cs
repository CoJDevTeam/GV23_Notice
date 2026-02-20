using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
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

      
        public PreviewDbDataService(IConfiguration cfg,AppDbContext db)
        {
            _db = db;
            _noticeDbConnStr = cfg.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Missing DefaultConnection for Notice_DB.");
        }

        // -----------------------
        // PUBLIC METHODS
        // -----------------------

        public async Task<S49PreviewDbData> S49PreviewDbDataAsync(int rollId, bool split, CancellationToken ct)
        {
            // 1) pick a roll row (single or split top1)
            var proc = split ? "dbo.S49_Preview_SelectSplitTop1" : "dbo.S49_Preview_SelectSingleTop1";

            var rollRow = await ExecSingleAsync(proc, cmd =>
            {
                cmd.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int) { Value = rollId });
            }, ct);

            if (rollRow is null)
                throw new InvalidOperationException("S49 preview: no roll row found.");

            var premiseId = rollRow.Str("PREMISEID") ?? rollRow.Str("PremiseId") ?? "";
            if (string.IsNullOrWhiteSpace(premiseId))
                throw new InvalidOperationException("S49 preview: missing PREMISEID.");

            // 2) fetch sap contact (you SHOULD have a proc for this; if not, create it and I’ll align it)
            // Expected proc name:
            // dbo.S49_Preview_SelectSapContactByPremise (@RollId, @PremiseId)
            var contactRow = await ExecSingleAsync("dbo.S49_Preview_SelectSapContactByPremise", cmd =>
            {
                cmd.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int) { Value = rollId });
                cmd.Parameters.Add(new SqlParameter("@PremiseId", SqlDbType.VarChar, 50) { Value = premiseId });
            }, ct);

            return new S49PreviewDbData
            {
                RollId = rollId,

                PremiseId = premiseId,
                PropertyDesc = rollRow.Str("PropertyDesc"),
                LisStreetAddress = rollRow.Str("LisStreetAddress"),
                ValuationKey = rollRow.Str("VALUATIONKEY") ?? rollRow.Str("ValuationKey"),
                CatDesc = rollRow.Str("CatDesc"),
                RateableArea = rollRow.Dec("RateableArea"),
                MarketValue = rollRow.Dec("MarketValue"),
                Reason = rollRow.Str("Reason"),
                ValuationSplitIndicator = rollRow.Str("ValuationSplitIndicator"),

                Email = contactRow?.Str("EMAIL_ADDR") ?? contactRow?.Str("Email"),
                Addr1 = contactRow?.Str("ADDR1"),
                Addr2 = contactRow?.Str("ADDR2"),
                Addr3 = contactRow?.Str("ADDR3"),
                Addr4 = contactRow?.Str("ADDR4"),
                Addr5 = contactRow?.Str("ADDR5"),
                PremiseAddress = contactRow?.Str("PREMISE_ADDRESS"),
                AccountNo = contactRow?.Str("ACCOUNT_NO")
            };
        }

        public async Task<S51PreviewDbData> S51PreviewDbDataAsync(int rollId, CancellationToken ct)
        {
            var row = await ExecSingleAsync("dbo.S51_Preview_SelectTop1", cmd =>
            {
                cmd.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int) { Value = rollId });
            }, ct);

            if (row is null)
                throw new InvalidOperationException("S51 preview: no data found.");

            return new S51PreviewDbData
            {
                RollId = rollId,
                ObjectionNo = row.Str("Objection_No") ?? row.Str("objection_No") ?? "",
                ObjectorType = row.Str("Objector_Type") ?? row.Str("objector_Type"),

                PremiseId = row.Str("Premise_iD") ?? row.Str("PremiseId"),
                PropertyDesc = row.Str("Property_desc") ?? row.Str("PropertyDesc"),
                Email = row.Str("Email") ?? row.Str("EMAIL_ADDR"),

                Addr1 = row.Str("ADDR1"),
                Addr2 = row.Str("ADDR2"),
                Addr3 = row.Str("ADDR3"),
                Addr4 = row.Str("ADDR4"),
                Addr5 = row.Str("ADDR5"),

                // Section6 (Old)
                OldCategory = row.Str("Old_Category"),
                Old2Category = row.Str("Old2_Category"),
                Old3Category = row.Str("Old3_Category"),

                OldExtent = row.Dec("Old_Extent"),
                Old2Extent = row.Dec("Old2_Extent"),
                Old3Extent = row.Dec("Old3_Extent"),

                OldMarketValue = row.Dec("Old_Market_Value"),
                Old2MarketValue = row.Dec("Old2_Market_Value"),
                Old3MarketValue = row.Dec("Old3_Market_Value"),

                // Section6 (New)
                NewCategory = row.Str("New_Category"),
                New2Category = row.Str("New2_Category"),
                New3Category = row.Str("New3_Category"),

                NewExtent = row.Dec("New_Extent"),
                New2Extent = row.Dec("New2_Extent"),
                New3Extent = row.Dec("New3_Extent"),

                NewMarketValue = row.Dec("New_Market_Value"),
                New2MarketValue = row.Dec("New2_Market_Value"),
                New3MarketValue = row.Dec("New3_Market_Value"),

                ObjectionReasons = row.Str("Objection_Reasons")
            };
        }

        public async Task<S52PreviewDbData> S52PreviewDbDataAsync(int rollId, string appealNo, bool isReview, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(appealNo))
                throw new ArgumentException("AppealNo is required.", nameof(appealNo));

            var proc = isReview
                ? "dbo.S52_Preview_SelectReviewTop1"
                : "dbo.S52_Preview_SelectAppealTop1";

            var row = await ExecSingleAsync(proc, cmd =>
            {
                cmd.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int) { Value = rollId });
                cmd.Parameters.Add(new SqlParameter("@AppealNo", SqlDbType.VarChar, 50) { Value = appealNo.Trim() });
            }, ct);

            if (row is null)
                throw new InvalidOperationException("S52 preview: no data found.");

            return new S52PreviewDbData
            {
                RollId = rollId,
                AppealNo = row.Str("Appeal_No") ?? appealNo,
                ObjectionNo = row.Str("Objection_No"),

                AUserId = row.Str("A_UserID"),
                PremiseId = row.Str("Premise_iD"),
                ValuationKey = row.Str("valuation_Key") ?? row.Str("VALUATIONKEY"),
                PropertyDesc = row.Str("Property_desc"),
                Email = row.Str("Email"),

                Addr1 = row.Str("ADDR1"),
                Addr2 = row.Str("ADDR2"),
                Addr3 = row.Str("ADDR3"),
                Addr4 = row.Str("ADDR4"),
                Addr5 = row.Str("ADDR5"),

                Town = row.Str("Town"),
                Erf = row.Str("ERF"),
                Ptn = row.Str("PTN"),
                Re = row.Str("RE"),

                AppMarketValue = row.Dec("App_Market_Value"),
                AppMarketValue2 = row.Dec("App_Market_Value2"),
                AppMarketValue3 = row.Dec("App_Market_Value3"),

                AppExtent = row.Dec("App_Extent"),
                AppExtent2 = row.Dec("App_Extent2"),
                AppExtent3 = row.Dec("App_Extent3"),

                AppCategory = row.Str("App_Category"),
                AppCategory2 = row.Str("App_Category2"),
                AppCategory3 = row.Str("App_Category3"),
            };
        }

        public async Task<S53PreviewDbData> S53PreviewDbDataAsync(int rollId, CancellationToken ct)
        {
            var row = await ExecSingleAsync("dbo.S53_Preview_SelectTop1", cmd =>
            {
                cmd.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int) { Value = rollId });
            }, ct);

            if (row is null)
                throw new InvalidOperationException("S53 preview: no data found.");

            return new S53PreviewDbData
            {
                RollId = rollId,
                ObjectionNo = row.Str("Objection_No") ?? "",

                PremiseId = row.Str("Premise_iD"),
                ValuationKey = row.Str("valuation_Key") ?? row.Str("VALUATIONKEY"),
                PropertyDesc = row.Str("Property_desc"),
                Email = row.Str("Email"),

                Addr1 = row.Str("ADDR1"),
                Addr2 = row.Str("ADDR2"),
                Addr3 = row.Str("ADDR3"),
                Addr4 = row.Str("ADDR4"),
                Addr5 = row.Str("ADDR5"),

                GvMarketValue = row.Dec("GVMarketValue") ?? row.Dec("GV_Market_Value"),
                GvMarketValue2 = row.Dec("GVMarketValue2") ?? row.Dec("GV_Market_Value2"),
                GvMarketValue3 = row.Dec("GVMarketValue3") ?? row.Dec("GV_Market_Value3"),

                GvExtent = row.Dec("GV_Extent"),
                GvExtent2 = row.Dec("GV_EXtent2") ?? row.Dec("GV_Extent2"),
                GvExtent3 = row.Dec("GV_Extent3"),

                GvCategory = row.Str("GV_Category"),
                GvCategory2 = row.Str("GV_Category2"),
                GvCategory3 = row.Str("GV_Category3"),

                MvdMarketValue = row.Dec("MVDMarketValue") ?? row.Dec("MVD_Market_Value"),
                MvdMarketValue2 = row.Dec("MVDMarketValue2") ?? row.Dec("MVD_Market_Value2"),
                MvdMarketValue3 = row.Dec("MVDMarketValue3") ?? row.Dec("MVD_Market_Value3"),

                MvdExtent = row.Dec("MVD_Extent"),
                MvdExtent2 = row.Dec("MVD_Extent2"),
                MvdExtent3 = row.Dec("MVD_Extent3"),

                MvdCategory = row.Str("MVD_Category"),
                MvdCategory2 = row.Str("MVD_Category2"),
                MvdCategory3 = row.Str("MVD_Category3"),

                Section52Review = row.Str("Section52Review"),
                BatchDate = row.Dt("Batch_Date"),
                AppealCloseDate = row.Dt("Appeal_Close_Date"),
                BatchName = row.Str("Batch_Name")
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

            return new DJPreviewDbData
            {
                RollId = rollId,
                ObjectionNo = row.Str("Objection_No") ?? "",
                PremiseId = row.Str("Premise_iD"),
                PropertyDesc = row.Str("Property_desc"),
                Email = row.Str("Email"),

                Addr1 = row.Str("ADDR1"),
                Addr2 = row.Str("ADDR2"),
                Addr3 = row.Str("ADDR3"),
                Addr4 = row.Str("ADDR4"),
                Addr5 = row.Str("ADDR5"),
            };
        }

        public async Task<InvalidPreviewDbData> InvalidPreviewDbDataAsync(int rollId, bool isOmission, CancellationToken ct)
        {
            var proc = isOmission
                ? "dbo.IN_Preview_SelectInvalidOmissionTop1"
                : "dbo.IN_Preview_SelectInvalidObjectionTop1";

            var row = await ExecSingleAsync(proc, cmd =>
            {
                cmd.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int) { Value = rollId });
            }, ct);

            if (row is null)
                throw new InvalidOperationException("Invalid preview: no invalid record found.");

            return new InvalidPreviewDbData
            {
                RollId = rollId,
                ObjectionNo = row.Str("Objection_No") ?? "",
                PremiseId = row.Str("Premise_iD"),
                PropertyDesc = row.Str("Property_desc"),
                Email = row.Str("Email"),

                Addr1 = row.Str("ADDR1"),
                Addr2 = row.Str("ADDR2"),
                Addr3 = row.Str("ADDR3"),
                Addr4 = row.Str("ADDR4"),
                Addr5 = row.Str("ADDR5"),

                ObjectionStatus = row.Str("Objection_Status")
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

    }
}

