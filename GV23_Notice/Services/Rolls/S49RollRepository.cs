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

        private async Task<(string connStr, string dbName, string rollTable, string sapTable)> ResolveAsync(int rollId, CancellationToken ct)
        {
            var roll = await _db.RollRegistry.AsNoTracking()
                .FirstOrDefaultAsync(r => r.RollId == rollId, ct);

            if (roll is null)
                throw new InvalidOperationException("RollRegistry not found.");

            if (string.IsNullOrWhiteSpace(roll.SourceDb))
                throw new InvalidOperationException("RollRegistry.SourceDb is missing.");

            var dbName = roll.SourceDb.Trim();

            // Build connection string: BaseSqlConnection + InitialCatalog=dbName
            var baseConn = _cfg["RollDb:BaseSqlConnection"];
            if (string.IsNullOrWhiteSpace(baseConn))
                throw new InvalidOperationException("Missing config: RollDb:BaseSqlConnection");

            var csb = new SqlConnectionStringBuilder(baseConn)
            {
                InitialCatalog = dbName
            };
            var connStr = csb.ConnectionString;

            // ✅ SAFE whitelist mapping (prevents injection via dynamic identifiers)
            var rollTable = _cfg[$"RollDb:RollTablesBySourceDb:{dbName}"];
            if (string.IsNullOrWhiteSpace(rollTable))
                throw new InvalidOperationException($"Missing config: RollDb:RollTablesBySourceDb:{dbName}");

            var sapTable = _cfg[$"RollDb:SapContactsTableBySourceDb:{dbName}"];
            if (string.IsNullOrWhiteSpace(sapTable))
                sapTable = "sapContacts";

            return (connStr, dbName, rollTable, sapTable);
        }

        public async Task<List<string>> PickNextPremiseIdsAsync(int rollId, int top, CancellationToken ct)
        {
            if (top <= 0) top = 500;
            if (top > 500) top = 500;

            var (connStr, dbName, rollTable, sapTable) = await ResolveAsync(rollId, ct);

            // Distinct premiseIds where Email_Sent = 0 and there is an email in sapContacts
            var sql = $@"
SELECT TOP (@Top) r.PREMISEID
FROM [{dbName}].[dbo].[{rollTable}] r
INNER JOIN [{dbName}].[dbo].[{sapTable}] c ON c.PREMISE_ID = r.PREMISEID
WHERE ISNULL(r.Email_Sent, 0) = 0
  AND NULLIF(LTRIM(RTRIM(c.EMAIL_ADDR)), '') IS NOT NULL
GROUP BY r.PREMISEID
ORDER BY MIN(r.Id) ASC;";

            var list = new List<string>();

            await using var cn = new SqlConnection(connStr);
            await cn.OpenAsync(ct);

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add(new SqlParameter("@Top", SqlDbType.Int) { Value = top });

            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var premiseId = rd.GetValue(0)?.ToString();
                if (!string.IsNullOrWhiteSpace(premiseId))
                    list.Add(premiseId.Trim());
            }

            return list;
        }

        public async Task<(List<S49RollRowDto> rows, SapContactDto? contact)> LoadPremiseAsync(int rollId, string premiseId, CancellationToken ct)
        {
            var (connStr, dbName, rollTable, sapTable) = await ResolveAsync(rollId, ct);

            var rollSql = $@"
SELECT
    CAST(r.PREMISEID as varchar(50)) as PremiseId,
    r.PropertyDesc,
    r.LisStreetAddress,
    r.CatDesc,
    TRY_CONVERT(decimal(18,2), r.MarketValue) as MarketValue,
    TRY_CONVERT(decimal(18,2), r.RateableArea) as Extent,
    r.Reason,
    ISNULL(r.Email_Sent, 0) as EmailSent
FROM [{dbName}].[dbo].[{rollTable}] r
WHERE r.PREMISEID = @PremiseId;";

            var contactSql = $@"
SELECT TOP (1)
    CAST(c.PREMISE_ID as varchar(50)) as PremiseId,
    c.EMAIL_ADDR as Email,
    c.ADDR1, c.ADDR2, c.ADDR3, c.ADDR4, c.ADDR5,
    c.PREMISE_ADDRESS as PremiseAddress,
    c.ACCOUNT_NO as AccountNo
FROM [{dbName}].[dbo].[{sapTable}] c
WHERE c.PREMISE_ID = @PremiseId;";

            var rows = new List<S49RollRowDto>();
            SapContactDto? contact = null;

            await using var cn = new SqlConnection(connStr);
            await cn.OpenAsync(ct);

            // Roll rows
            await using (var cmd = new SqlCommand(rollSql, cn))
            {
                cmd.Parameters.Add(new SqlParameter("@PremiseId", SqlDbType.VarChar, 50) { Value = premiseId });

                await using var rd = await cmd.ExecuteReaderAsync(ct);
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
                        EmailSent = rd["EmailSent"] == DBNull.Value ? 0 : Convert.ToInt32(rd["EmailSent"])
                    });
                }
            }

            // Contact
            await using (var cmd = new SqlCommand(contactSql, cn))
            {
                cmd.Parameters.Add(new SqlParameter("@PremiseId", SqlDbType.VarChar, 50) { Value = premiseId });

                await using var rd = await cmd.ExecuteReaderAsync(ct);
                if (await rd.ReadAsync(ct))
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
            }

            return (rows, contact);
        }

        public async Task MarkEmailSentAsync(int rollId, string premiseId, CancellationToken ct)
        {
            var (connStr, dbName, rollTable, _) = await ResolveAsync(rollId, ct);

            var sql = $@"
UPDATE [{dbName}].[dbo].[{rollTable}]
SET Email_Sent = 1
WHERE PREMISEID = @PremiseId;";

            await using var cn = new SqlConnection(connStr);
            await cn.OpenAsync(ct);

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add(new SqlParameter("@PremiseId", SqlDbType.VarChar, 50) { Value = premiseId });

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
