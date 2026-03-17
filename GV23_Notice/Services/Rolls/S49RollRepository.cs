using GV23_Notice.Data;
using GV23_Notice.Models.DTOs;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace GV23_Notice.Services.Rolls
{
    public sealed class S49RollRepository : IS49RollRepository
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _cfg;

        public S49RollRepository(AppDbContext db, IConfiguration cfg)
        {
            _db = db;
            _cfg = cfg;
        }

        // ── Notice_DB connection (all SPs live here) ─────────────────────────
        private SqlConnection NoticeDb()
        {
            var cs = _cfg.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection is not configured.");
            return new SqlConnection(cs);
        }

        // ── ResolveAsync: kept only for legacy callers that still need the
        //   roll-DB connStr (e.g. direct ADO queries outside this service). ───
        private async Task<(string connStr, string dbName, string rollTable, string sapTable)>
            ResolveAsync(int rollId, CancellationToken ct)
        {
            var roll = await _db.RollRegistry.AsNoTracking()
                .FirstOrDefaultAsync(r => r.RollId == rollId, ct)
                ?? throw new InvalidOperationException("RollRegistry not found.");

            if (string.IsNullOrWhiteSpace(roll.SourceDb))
                throw new InvalidOperationException("RollRegistry.SourceDb is missing.");

            var dbName = roll.SourceDb.Trim();

            var baseConn = _cfg["RollDb:BaseSqlConnection"];
            if (string.IsNullOrWhiteSpace(baseConn))
                throw new InvalidOperationException("Missing config: RollDb:BaseSqlConnection");

            var csb = new SqlConnectionStringBuilder(baseConn) { InitialCatalog = dbName };
            var connStr = csb.ConnectionString;

            var rollTable = _cfg[$"RollDb:RollTablesBySourceDb:{dbName}"];
            if (string.IsNullOrWhiteSpace(rollTable))
                throw new InvalidOperationException(
                    $"Missing config: RollDb:RollTablesBySourceDb:{dbName}");

            var sapTable = _cfg[$"RollDb:SapContactsTableBySourceDb:{dbName}"];
            if (string.IsNullOrWhiteSpace(sapTable))
                sapTable = "sapContacts";

            return (connStr, dbName, rollTable, sapTable);
        }

        // ── PickNextPremiseIdsAsync ──────────────────────────────────────────
        // Used for PREVIEW (top 1) and legacy batch paths.
        // This is READ-ONLY — it does NOT assign Batch_Name or Batch_Date.
        // Actual batch creation goes through S49_Step3_AssignTop500ToBatch SP
        // (called via EF in Step3BatchService) which stamps Batch_Name/Batch_Date.
        public async Task<List<string>> PickNextPremiseIdsAsync(
            int rollId, int top, CancellationToken ct)
        {
            if (top <= 0) top = 500;
            if (top > 500) top = 500;

            var (connStr, dbName, rollTable, sapTable) = await ResolveAsync(rollId, ct);

            // Read-only SELECT — no Batch_Name stamping.
            // Filters: unprocessed (Email_Sent not in P/Y/N/NP) AND not yet batched (Batch_Name IS NULL).
            var sql = $@"
SELECT TOP (@Top) r.PREMISEID
FROM  [{dbName}].[dbo].[{rollTable}] r
INNER JOIN [{dbName}].[dbo].[{sapTable}] c
       ON  c.PREMISE_ID = r.PREMISEID
WHERE (
        r.Email_Sent IS NULL
     OR CAST(r.Email_Sent AS VARCHAR(10)) = '0'
     OR CAST(r.Email_Sent AS VARCHAR(10)) NOT IN ('P','Y','N','NP')
      )
  AND r.Batch_Name IS NULL
  AND NULLIF(LTRIM(RTRIM(c.EMAIL_ADDR)), '') IS NOT NULL
GROUP BY r.PREMISEID
ORDER BY MIN(r.Id) ASC;";

            var list = new List<string>();

            await using var cn = new SqlConnection(connStr);
            await cn.OpenAsync(ct);

            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
            cmd.Parameters.Add(new SqlParameter("@Top", SqlDbType.Int) { Value = top });

            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var premiseId = rd.GetValue(0)?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(premiseId))
                    list.Add(premiseId);
            }

            return list;
        }

        // ── LoadPremiseAsync ─────────────────────────────────────────────────
        // SP: dbo.S49_Step3_LoadPremise  (two result sets)
        // Replaces the two interpolated SELECT statements that concatenated
        // {dbName}, {rollTable}, {sapTable} directly into SQL strings.
        public async Task<(List<S49RollRowDto> rows, SapContactDto? contact)>
            LoadPremiseAsync(int rollId, string premiseId, CancellationToken ct)
        {
            var rows = new List<S49RollRowDto>();
            SapContactDto? contact = null;

            await using var cn = NoticeDb();
            await cn.OpenAsync(ct);

            await using var cmd = new SqlCommand("dbo.S49_Step3_LoadPremise", cn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };
            cmd.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int) { Value = rollId });
            cmd.Parameters.Add(new SqlParameter("@PremiseId", SqlDbType.VarChar, 50) { Value = premiseId });

            await using var rd = await cmd.ExecuteReaderAsync(ct);

            // ── Result Set 1: roll rows ──────────────────────────────────────
            while (await rd.ReadAsync(ct))
            {
                rows.Add(new S49RollRowDto
                {
                    PremiseId = rd["PremiseId"]?.ToString() ?? premiseId,
                    PropertyDesc = rd["PropertyDesc"]?.ToString(),
                    LisStreetAddress = rd["LisStreetAddress"]?.ToString(),
                    CatDesc = rd["CatDesc"]?.ToString(),
                    MarketValue = rd["MarketValue"] == DBNull.Value ? 0 : (decimal)rd["MarketValue"],
                    Extent = rd["Extent"] == DBNull.Value ? 0 : (decimal)rd["Extent"],
                    Reason = rd["Reason"]?.ToString(),
                    EmailSent = rd["EmailSent"] == DBNull.Value ? 0
                                           : int.TryParse(rd["EmailSent"]?.ToString(), out var es) ? es : 1
                });
            }

            // ── Result Set 2: sap contact ────────────────────────────────────
            if (await rd.NextResultAsync(ct) && await rd.ReadAsync(ct))
            {
                contact = new SapContactDto
                {
                    PremiseId = rd["PremiseId"]?.ToString() ?? premiseId,
                    Email = rd["Email"]?.ToString(),
                    Addr1 = rd["ADDR1"]?.ToString(),
                    Addr2 = rd["ADDR2"]?.ToString(),
                    Addr3 = rd["ADDR3"]?.ToString(),
                    Addr4 = rd["ADDR4"]?.ToString(),
                    Addr5 = rd["ADDR5"]?.ToString(),
                    PremiseAddress = rd["PremiseAddress"]?.ToString(),
                    AccountNo = rd["AccountNo"]?.ToString()
                };
            }

            return (rows, contact);
        }

        // ── MarkPrintingAsync ────────────────────────────────────────────────
        // SP: dbo.S49_Step3_MarkPrinting  — sets Email_Sent = 'P'
        // Called before PDF generation begins.
        public Task MarkPrintingAsync(int rollId, string premiseId, CancellationToken ct)
            => ExecStatusSpAsync("dbo.S49_Step3_MarkPrinting", rollId, premiseId, ct);

        // ── MarkPrintFailedAsync ─────────────────────────────────────────────
        // SP: dbo.S49_Step3_MarkPrintFailed  — sets Email_Sent = 'NP'
        // Called when PDF generation throws.
        public Task MarkPrintFailedAsync(int rollId, string premiseId, CancellationToken ct)
            => ExecStatusSpAsync("dbo.S49_Step3_MarkPrintFailed", rollId, premiseId, ct);

        // ── MarkEmailSentAsync ───────────────────────────────────────────────
        // SP: dbo.S49_Step3_MarkEmailSent  — sets Email_Sent = 'Y'
        // Replaces the raw UPDATE with interpolated {dbName}/{rollTable}.
        public Task MarkEmailSentAsync(int rollId, string premiseId, CancellationToken ct)
            => ExecStatusSpAsync("dbo.S49_Step3_MarkEmailSent", rollId, premiseId, ct);

        // ── MarkEmailFailedAsync ─────────────────────────────────────────────
        // SP: dbo.S49_Step3_MarkEmailFailed  — sets Email_Sent = 'N'
        public Task MarkEmailFailedAsync(int rollId, string premiseId, CancellationToken ct)
            => ExecStatusSpAsync("dbo.S49_Step3_MarkEmailFailed", rollId, premiseId, ct);

        // ── Shared helper for all status SPs ─────────────────────────────────
        private async Task ExecStatusSpAsync(
            string spName, int rollId, string premiseId, CancellationToken ct)
        {
            await using var cn = NoticeDb();
            await cn.OpenAsync(ct);

            await using var cmd = new SqlCommand(spName, cn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 30
            };
            cmd.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int) { Value = rollId });
            cmd.Parameters.Add(new SqlParameter("@PremiseId", SqlDbType.VarChar, 50) { Value = premiseId });

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}