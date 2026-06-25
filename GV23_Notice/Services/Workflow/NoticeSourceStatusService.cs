using GV23_Notice.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services.Workflow
{
    public sealed class NoticeSourceStatusService : INoticeSourceStatusService
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;

        public NoticeSourceStatusService(AppDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        public async Task<bool> IsS53StatusAsync(
            int rollId,
            string objectionNo,
            string expectedStatus,
            CancellationToken ct)
        {
            var sourceDb = await GetSourceDbAsync(rollId, ct);

            var cs = _config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection missing.");

            await using var con = new SqlConnection(cs);
            await con.OpenAsync(ct);

            var sql = $@"
SELECT COUNT(1)
FROM {QuoteDb(sourceDb)}.[dbo].[Obj_Property_Info]
WHERE LTRIM(RTRIM(ISNULL(Objection_No, ''))) = @ObjectionNo
  AND LTRIM(RTRIM(ISNULL(objection_Status, ''))) = @ExpectedStatus;";

            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@ObjectionNo", objectionNo.Trim());
            cmd.Parameters.AddWithValue("@ExpectedStatus", expectedStatus.Trim());

            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            return count > 0;
        }

        public async Task SetS53StatusAsync(
            int rollId,
            IEnumerable<string> objectionNos,
            string newStatus,
            CancellationToken ct)
        {
            var refs = objectionNos
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!refs.Any())
                return;

            var sourceDb = await GetSourceDbAsync(rollId, ct);

            var cs = _config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection missing.");

            await using var con = new SqlConnection(cs);
            await con.OpenAsync(ct);

            var tempSql = "CREATE TABLE #Refs (Objection_No NVARCHAR(80) NOT NULL PRIMARY KEY);";
            await using (var tempCmd = new SqlCommand(tempSql, con))
                await tempCmd.ExecuteNonQueryAsync(ct);

            foreach (var r in refs)
            {
                await using var insertCmd = new SqlCommand(
                    "INSERT INTO #Refs (Objection_No) VALUES (@ObjectionNo);", con);

                insertCmd.Parameters.AddWithValue("@ObjectionNo", r);
                await insertCmd.ExecuteNonQueryAsync(ct);
            }

            var updateSql = $@"
UPDATE a
SET a.objection_Status = @NewStatus
FROM {QuoteDb(sourceDb)}.[dbo].[Obj_Property_Info] a
INNER JOIN #Refs r
    ON LTRIM(RTRIM(a.Objection_No)) = LTRIM(RTRIM(r.Objection_No));";

            await using var updateCmd = new SqlCommand(updateSql, con);
            updateCmd.Parameters.AddWithValue("@NewStatus", newStatus.Trim());

            await updateCmd.ExecuteNonQueryAsync(ct);
        }

        private async Task<string> GetSourceDbAsync(int rollId, CancellationToken ct)
        {
            var sourceDb = await _db.RollRegistry
                .AsNoTracking()
                .Where(x => x.RollId == rollId)
                .Select(x => x.SourceDb)
                .FirstOrDefaultAsync(ct);

            if (string.IsNullOrWhiteSpace(sourceDb))
                throw new InvalidOperationException($"No SourceDb found for RollId {rollId}.");

            return sourceDb.Trim();
        }

        private static string QuoteDb(string dbName)
        {
            return "[" + dbName.Replace("]", "]]") + "]";
        }
    }
}