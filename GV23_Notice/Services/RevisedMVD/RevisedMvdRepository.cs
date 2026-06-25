using GV23_Notice.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using Dapper;

namespace GV23_Notice.Services.RevisedMVD
{
    public sealed class RevisedMvdRepository : IRevisedMvdRepository
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;

        public RevisedMvdRepository(
            AppDbContext db,
            IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        public async Task<List<dynamic>> GetDashboardTotalsAsync(int rollId, CancellationToken ct)
        {
            var result = new List<dynamic>();

            result.AddRange(await QueryAsync(
                "dbo.AllRevisedMVD_Procedure",
                new { RollId = rollId },
                ct));

            result.AddRange(await QueryAsync(
                "dbo.AllRevisedMVD_NotSent_Procedure",
                new { RollId = rollId },
                ct));

            result.AddRange(await QueryAsync(
                "dbo.EmailToSend_RevisedMVD_Procedure",
                new { RollId = rollId },
                ct));

            result.AddRange(await QueryAsync(
                "dbo.AllRevisedMVD_Sector_Procedure",
                new { RollId = rollId },
                ct));

            result.AddRange(await QueryAsync(
                "dbo.AllRevisedMVD_Objector_Procedure",
                new { RollId = rollId },
                ct));

            return result;
        }

        public Task<List<dynamic>> SearchToSendAsync(
            int rollId,
            string? searchString,
            string? searchBy,
            CancellationToken ct)
        {
            return QueryAsync(
                "dbo.SearchToSend_RevisedMVD",
                new
                {
                    RollId = rollId,
                    searchString,
                    searchBy
                },
                ct);
        }

        public Task<List<dynamic>> SearchSentAsync(
            int rollId,
            string? searchString,
            string? searchBy,
            CancellationToken ct)
        {
            return QueryAsync(
                "dbo.Resend_Email_RevisedMVD",
                new
                {
                    RollId = rollId,
                    searchString,
                    searchBy
                },
                ct);
        }

        public Task<List<dynamic>> InsertOwnerRevisedMvdAsync(
            int rollId,
            string objectionNo,
            CancellationToken ct)
        {
            return QueryAsync(
                "dbo.Insert_Owner_RevisedMVD",
                new
                {
                    RollId = rollId,
                    objectionNo
                },
                ct);
        }

        public Task<List<dynamic>> InsertSecondRevisedMvdAsync(
            int rollId,
            string objectionNo,
            CancellationToken ct)
        {
            return QueryAsync(
                "dbo.Insert_Second_RevisedMVD",
                new
                {
                    RollId = rollId,
                    objectionNo
                },
                ct);
        }

        public Task MarkMvdSentAsync(
            int rollId,
            string objectionNo,
            CancellationToken ct)
        {
            return ExecuteAsync(
                "dbo.MVD_Sent",
                new
                {
                    RollId = rollId,
                    objectionNo
                },
                ct);
        }

        public Task MarkRevisedMvdEmailsSentAsync(
            int rollId,
            string objectionNo,
            CancellationToken ct)
        {
            return ExecuteAsync(
                "dbo.ReviseMVDEmails_Sent",
                new
                {
                    RollId = rollId,
                    Objection_No = objectionNo
                },
                ct);
        }

        public Task MarkMailDoneRevisedMvdAsync(
            int rollId,
            string objectionNo,
            CancellationToken ct)
        {
            return ExecuteAsync(
                "dbo.MAIL_Done_ReviseMVD",
                new
                {
                    RollId = rollId,
                    Objection_No = objectionNo
                },
                ct);
        }

        public Task PrintReviseMvdAsync(int rollId, CancellationToken ct)
        {
            return ExecuteAsync(
                "dbo.Print_ReviseMVD",
                new { RollId = rollId },
                ct);
        }

        public Task BatchDateReviseMvdAsync(int rollId, CancellationToken ct)
        {
            return ExecuteAsync(
                "dbo.Batch_date_ReviseMVD",
                new { RollId = rollId },
                ct);
        }

        public Task BatchUpdateAsync(int rollId, CancellationToken ct)
        {
            return ExecuteAsync(
                "dbo.Batch_Update",
                new { RollId = rollId },
                ct);
        }

        public Task ReviseMvdPrintDoneAsync(int rollId, CancellationToken ct)
        {
            return ExecuteAsync(
                "dbo.ReviseMVD_PrintDone",
                new { RollId = rollId },
                ct);
        }

        public Task CancelReviseMvdAsync(int rollId, CancellationToken ct)
        {
            return ExecuteAsync(
                "dbo.CancelRevise_MVD",
                new { RollId = rollId },
                ct);
        }

        public Task GvDataReviseMvdAsync(int rollId, CancellationToken ct)
        {
            return ExecuteAsync(
                "dbo.GV_Data_ReviseMVD",
                new { RollId = rollId },
                ct);
        }

        public Task Section52ReviewReviseMvdAsync(int rollId, CancellationToken ct)
        {
            return ExecuteAsync(
                "dbo.Section52Review_ReviseMVD",
                new { RollId = rollId },
                ct);
        }

        public Task<List<dynamic>> SendEmailReviseMvdAsync(
            int rollId,
            CancellationToken ct)
        {
            return QueryAsync(
                "dbo.Send_Email_ReviseMVD",
                new { RollId = rollId },
                ct);
        }

        public Task<List<dynamic>> SaveReviseMvdNoticeAsync(
            int rollId,
            CancellationToken ct)
        {
            return QueryAsync(
                "dbo.Save_ReviseMVD_Notice",
                new { RollId = rollId },
                ct);
        }

        public Task<List<dynamic>> ReviseMvdPrintAsync(
            int rollId,
            CancellationToken ct)
        {
            return QueryAsync(
                "dbo.ReviseMVD_PRINT",
                new { RollId = rollId },
                ct);
        }

        private async Task<List<dynamic>> QueryAsync(
            string procedureName,
            object parameters,
            CancellationToken ct)
        {
            var cs = _db.Database.GetConnectionString()
                     ?? _config.GetConnectionString("DefaultConnection")
                     ?? throw new InvalidOperationException("DefaultConnection not configured.");

            await using var conn = new SqlConnection(cs);
            await conn.OpenAsync(ct);

            var rows = await conn.QueryAsync(
                sql: procedureName,
                param: parameters,
                commandType: CommandType.StoredProcedure,
                commandTimeout: 180);

            return rows.ToList();
        }

        private async Task ExecuteAsync(
            string procedureName,
            object parameters,
            CancellationToken ct)
        {
            var cs = _db.Database.GetConnectionString()
                     ?? _config.GetConnectionString("DefaultConnection")
                     ?? throw new InvalidOperationException("DefaultConnection not configured.");

            await using var conn = new SqlConnection(cs);
            await conn.OpenAsync(ct);

            await conn.ExecuteAsync(
                sql: procedureName,
                param: parameters,
                commandType: CommandType.StoredProcedure,
                commandTimeout: 180);
        }
    }
}