using GV23_Notice.Data;
using GV23_Notice.Models.Workflow.ViewModels;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;

namespace GV23_Notice.Services.ThirdPartyApplications
{
    public sealed class ThirdPartyAppealDateConfigurationService : IThirdPartyAppealDateConfigurationService
    {
        private const string NoticeName = "Third-Party Appeal Application";

        private readonly AppDbContext _db;

        public ThirdPartyAppealDateConfigurationService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<ThirdPartyAppealDateConfigVm> BuildAsync(
            int? rollId,
            string? rollShortCode,
            string? valuationPeriod,
            string performedBy,
            CancellationToken ct)
        {
            var selectedValuationPeriod = string.IsNullOrWhiteSpace(valuationPeriod)
                ? "GENERAL VALUATION ROLL 2023"
                : valuationPeriod.Trim();

            var selectedRollShortCode = string.IsNullOrWhiteSpace(rollShortCode)
                ? null
                : rollShortCode.Trim();

            var summary = await GetSummaryFromSpAsync(
                rollId,
                selectedRollShortCode,
                selectedValuationPeriod,
                performedBy,
                ct);

            var vm = new ThirdPartyAppealDateConfigVm
            {
                RollId = rollId,
                RollShortCode = selectedRollShortCode ?? summary.RollShortCode,
                Notice = NoticeName,
                ValuationPeriod = selectedValuationPeriod,

                EstimatedSendDate = summary.EstimatedSendDate,
                ResponseDays = summary.ResponseDays,
                EstimatedResponseDueDate = summary.EstimatedResponseDueDate,

                TotalRecords = summary.TotalRecords,
                TotalWithOwnerEmail = summary.TotalWithOwnerEmail,
                TotalMissingOwnerEmail = summary.TotalMissingOwnerEmail,

                SingleCount = summary.SingleCount,
                MultipurposeCount = summary.MultipurposeCount,

                TotalPrinted = summary.TotalPrinted,
                TotalSent = summary.TotalSent,
                TotalFailed = summary.TotalFailed,

                Rolls = await BuildRollsAsync(rollId, ct),
                ValuationPeriods = BuildValuationPeriods(selectedValuationPeriod)
            };

            return vm;
        }

        public async Task SaveStep1AuditAsync(
            ThirdPartyAppealDateConfigVm vm,
            string performedBy,
            CancellationToken ct)
        {
            if (vm == null)
                throw new ArgumentNullException(nameof(vm));

            if (string.IsNullOrWhiteSpace(vm.ValuationPeriod))
                throw new InvalidOperationException("Valuation period is required.");

            var snapshot = JsonSerializer.Serialize(new
            {
                vm.NoticeSettingsId,
                vm.RollId,
                vm.RollShortCode,
                vm.Notice,
                vm.ValuationPeriod,
                vm.EstimatedSendDate,
                vm.ResponseDays,
                vm.EstimatedResponseDueDate,
                vm.TotalRecords,
                vm.TotalWithOwnerEmail,
                vm.TotalMissingOwnerEmail,
                vm.SingleCount,
                vm.MultipurposeCount,
                vm.TotalPrinted,
                vm.TotalSent,
                vm.TotalFailed
            });

            var connection = _db.Database.GetDbConnection();

            await using var command = connection.CreateCommand();
            command.CommandText = "[dbo].[usp_ThirdPartyAppeal_SaveStep1DateConfigurationAudit]";
            command.CommandType = CommandType.StoredProcedure;

            command.Parameters.Add(new SqlParameter("@NoticeSettingsId", SqlDbType.Int)
            {
                Value = (object?)vm.NoticeSettingsId ?? DBNull.Value
            });

            command.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int)
            {
                Value = (object?)vm.RollId ?? DBNull.Value
            });

            command.Parameters.Add(new SqlParameter("@Notice", SqlDbType.NVarChar, 150)
            {
                Value = vm.Notice
            });

            command.Parameters.Add(new SqlParameter("@ValuationPeriod", SqlDbType.NVarChar, 250)
            {
                Value = vm.ValuationPeriod
            });

            command.Parameters.Add(new SqlParameter("@EstimatedSendDate", SqlDbType.Date)
            {
                Value = vm.EstimatedSendDate.Date
            });

            command.Parameters.Add(new SqlParameter("@ResponseDays", SqlDbType.Int)
            {
                Value = vm.ResponseDays
            });

            command.Parameters.Add(new SqlParameter("@EstimatedResponseDueDate", SqlDbType.Date)
            {
                Value = vm.EstimatedResponseDueDate.Date
            });

            command.Parameters.Add(new SqlParameter("@PerformedBy", SqlDbType.NVarChar, 200)
            {
                Value = string.IsNullOrWhiteSpace(performedBy) ? "Unknown" : performedBy
            });

            command.Parameters.Add(new SqlParameter("@SnapshotJson", SqlDbType.NVarChar, -1)
            {
                Value = snapshot
            });

            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(ct);

            await command.ExecuteNonQueryAsync(ct);
        }

        private async Task<ThirdPartyAppealDateConfigurationSummaryVm> GetSummaryFromSpAsync(
            int? rollId,
            string? rollShortCode,
            string valuationPeriod,
            string performedBy,
            CancellationToken ct)
        {
            var connection = _db.Database.GetDbConnection();

            await using var command = connection.CreateCommand();
            command.CommandText = "[dbo].[usp_ThirdPartyAppeal_GetDateConfigurationSummary]";
            command.CommandType = CommandType.StoredProcedure;

            command.Parameters.Add(new SqlParameter("@RollId", SqlDbType.Int)
            {
                Value = (object?)rollId ?? DBNull.Value
            });

            command.Parameters.Add(new SqlParameter("@RollShortCode", SqlDbType.NVarChar, 50)
            {
                Value = string.IsNullOrWhiteSpace(rollShortCode) ? DBNull.Value : rollShortCode
            });

            command.Parameters.Add(new SqlParameter("@ValuationPeriod", SqlDbType.NVarChar, 250)
            {
                Value = valuationPeriod
            });

            command.Parameters.Add(new SqlParameter("@Notice", SqlDbType.NVarChar, 150)
            {
                Value = NoticeName
            });

            command.Parameters.Add(new SqlParameter("@ConfiguredBy", SqlDbType.NVarChar, 200)
            {
                Value = string.IsNullOrWhiteSpace(performedBy) ? "Unknown" : performedBy
            });

            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(ct);

            await using var reader = await command.ExecuteReaderAsync(ct);

            if (!await reader.ReadAsync(ct))
            {
                return new ThirdPartyAppealDateConfigurationSummaryVm
                {
                    RollId = rollId,
                    RollShortCode = rollShortCode ?? "",
                    ValuationPeriod = valuationPeriod,
                    Notice = NoticeName,
                    EstimatedSendDate = DateTime.Today,
                    ResponseDays = 51,
                    EstimatedResponseDueDate = DateTime.Today.AddDays(51)
                };
            }

            return new ThirdPartyAppealDateConfigurationSummaryVm
            {
                RollId = ReadNullableInt(reader, "RollId"),
                RollShortCode = ReadString(reader, "RollShortCode"),
                ValuationPeriod = ReadString(reader, "ValuationPeriod"),
                Notice = ReadString(reader, "Notice"),

                EstimatedSendDate = ReadDate(reader, "EstimatedSendDate", DateTime.Today),
                ResponseDays = ReadInt(reader, "ResponseDays"),
                EstimatedResponseDueDate = ReadDate(reader, "EstimatedResponseDueDate", DateTime.Today.AddDays(51)),

                TotalRecords = ReadInt(reader, "TotalRecords"),
                TotalWithOwnerEmail = ReadInt(reader, "TotalWithOwnerEmail"),
                TotalMissingOwnerEmail = ReadInt(reader, "TotalMissingOwnerEmail"),

                SingleCount = ReadInt(reader, "SingleCount"),
                MultipurposeCount = ReadInt(reader, "MultipurposeCount"),

                TotalPrinted = ReadInt(reader, "TotalPrinted"),
                TotalSent = ReadInt(reader, "TotalSent"),
                TotalFailed = ReadInt(reader, "TotalFailed")
            };
        }

        private async Task<List<SelectListItem>> BuildRollsAsync(
            int? selectedRollId,
            CancellationToken ct)
        {
            /*
             * Replace this with your existing RollRegistry table if the property names differ.
             * I kept it simple because your app already has RollRegistry in other notice workflows.
             */
            var rolls = await _db.RollRegistry  
                .AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => new SelectListItem
                {
                    Value = x.RollId.ToString(),
                    Text = x.ShortCode + " - " + x.Name,
                    Selected = selectedRollId.HasValue && x.RollId == selectedRollId.Value
                })
                .ToListAsync(ct);

            return rolls;
        }

        private static List<SelectListItem> BuildValuationPeriods(string selected)
        {
            var periods = new[]
            {
                "GENERAL VALUATION ROLL 2023",
                "SUPPLEMENTARY VALUATION ROLL 1",
                "SUPPLEMENTARY VALUATION ROLL 2",
                "SUPPLEMENTARY VALUATION ROLL 3"
            };

            return periods
                .Select(x => new SelectListItem
                {
                    Value = x,
                    Text = x,
                    Selected = string.Equals(x, selected, StringComparison.OrdinalIgnoreCase)
                })
                .ToList();
        }

        private static string ReadString(IDataRecord reader, string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? "" : Convert.ToString(reader.GetValue(ordinal)) ?? "";
        }

        private static int ReadInt(IDataRecord reader, string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
        }

        private static int? ReadNullableInt(IDataRecord reader, string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
        }

        private static DateTime ReadDate(IDataRecord reader, string name, DateTime fallback)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? fallback : Convert.ToDateTime(reader.GetValue(ordinal));
        }
    }
}
