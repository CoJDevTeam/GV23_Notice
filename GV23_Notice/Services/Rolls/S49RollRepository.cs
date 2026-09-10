using GV23_Notice.Models.DTOs;
using GV23_Notice.Models.DTOs.GV23_Notice.Models.DTOs;
using Microsoft.Data.SqlClient;
using System.Data;

namespace GV23_Notice.Services.Rolls
{
    /// <summary>
    /// Section 49 repository.
    ///
    /// IMPORTANT:
    /// All SQL lives in Notice_DB stored procedures.
    /// This service contains no dynamic SQL and no roll-specific table names.
    ///
    /// Roll routing (GV23 / SUPP1 / SUPP2 / SUPP3 / SUPP4 / future rolls)
    /// is handled inside the stored procedures.
    /// </summary>
    public sealed class S49RollRepository : IS49RollRepository
    {
        private readonly IConfiguration _cfg;

        public S49RollRepository(IConfiguration cfg)
        {
            _cfg = cfg;
        }

        // ============================================================
        // NOTICE_DB CONNECTION
        // ============================================================

        private SqlConnection NoticeDb()
        {
            var cs =
                _cfg.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "DefaultConnection is not configured.");

            return new SqlConnection(cs);
        }

        // ============================================================
        // PICK NEXT PREMISES
        // ============================================================

        public async Task<List<string>> PickNextPremiseIdsAsync(
            int rollId,
            int top,
            CancellationToken ct)
        {
            if (top <= 0)
                top = 500;

            if (top > 500)
                top = 500;

            var result =
                new List<string>();

            await using var cn =
                NoticeDb();

            await cn.OpenAsync(ct);

            await using var cmd =
                new SqlCommand(
                    "dbo.S49_Step3_PickNextPremises",
                    cn)
                {
                    CommandType =
                        CommandType.StoredProcedure,

                    CommandTimeout =
                        120
                };

            cmd.Parameters.Add(
                new SqlParameter(
                    "@RollId",
                    SqlDbType.Int)
                {
                    Value = rollId
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@Top",
                    SqlDbType.Int)
                {
                    Value = top
                });

            await using var rd =
                await cmd.ExecuteReaderAsync(ct);

            while (await rd.ReadAsync(ct))
            {
                var premiseId =
                    GetReaderString(
                        rd,
                        "PremiseId");

                if (!string.IsNullOrWhiteSpace(
                        premiseId))
                {
                    result.Add(
                        premiseId);
                }
            }

            return result;
        }

        // ============================================================
        // LOAD PREMISE
        // ============================================================

        public async Task<
            (
                List<S49RollRowDto> rows,
                SapContactDto? contact
            )>
            LoadPremiseAsync(
                int rollId,
                string premiseId,
                CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(
                    premiseId))
            {
                throw new ArgumentException(
                    "PremiseId is required.",
                    nameof(premiseId));
            }

            var rows =
                new List<S49RollRowDto>();

            SapContactDto? contact =
                null;

            await using var cn =
                NoticeDb();

            await cn.OpenAsync(ct);

            await using var cmd =
                new SqlCommand(
                    "dbo.S49_Step3_LoadPremise",
                    cn)
                {
                    CommandType =
                        CommandType.StoredProcedure,

                    CommandTimeout =
                        120
                };

            cmd.Parameters.Add(
                new SqlParameter(
                    "@RollId",
                    SqlDbType.Int)
                {
                    Value = rollId
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@PremiseId",
                    SqlDbType.VarChar,
                    50)
                {
                    Value = premiseId.Trim()
                });

            await using var rd =
                await cmd.ExecuteReaderAsync(ct);

            // Result set 1 = roll rows
            while (await rd.ReadAsync(ct))
            {
                rows.Add(
                    MapRollRow(
                        rd,
                        premiseId));
            }

            // Result set 2 = contact row
            if (
                await rd.NextResultAsync(ct)
                &&
                await rd.ReadAsync(ct))
            {
                contact =
                    MapContact(
                        rd,
                        premiseId);
            }

            rows =
    NormaliseSection49Rows(
        rows);
            return (
                rows,
                contact);
        }

        private static List<S49RollRowDto>
    NormaliseSection49Rows(
        List<S49RollRowDto> rows)
        {
            if (rows == null ||
                rows.Count <= 1)
            {
                return rows
                    ?? new List<S49RollRowDto>();
            }

            var propertyDesc =
                rows
                    .FirstOrDefault()?
                    .PropertyDesc?
                    .Trim()
                ?? "";

            var isFullTitleLongTermLease =
                propertyDesc.Contains(
                    "FULL TITLE LONG-TERM LEASE",
                    StringComparison.OrdinalIgnoreCase);

            if (!isFullTitleLongTermLease)
            {
                return rows;
            }

            /*
             * FULL TITLE LONG-TERM LEASE BUSINESS RULE
             *
             * This property must be displayed as a
             * standalone valuation — never as a
             * multipurpose/split table.
             *
             * Prefer:
             * 1. A non-zero market value
             * 2. Latest effective date
             * 3. Highest/latest valuation key
             */
            var selected =
                rows
                    .Where(x =>
                        x.MarketValue > 0)
                    .OrderByDescending(x =>
                        x.WEFDate
                        ?? DateTime.MinValue)
                    .ThenByDescending(x =>
                    {
                        return long.TryParse(
                            x.ValuationKey,
                            out var key)
                                ? key
                                : 0L;
                    })
                    .FirstOrDefault();

            /*
             * Safety fallback:
             * if no non-zero record exists,
             * use the latest available valuation.
             */
            selected ??=
                rows
                    .OrderByDescending(x =>
                        x.WEFDate
                        ?? DateTime.MinValue)
                    .ThenByDescending(x =>
                    {
                        return long.TryParse(
                            x.ValuationKey,
                            out var key)
                                ? key
                                : 0L;
                    })
                    .First();

            return new List<S49RollRowDto>
    {
        selected
    };
        }
        // ============================================================
        // ASSIGN BATCH
        // ============================================================

        public async Task<List<S49BatchPickRow>> AssignBatchAsync(
            int rollId,
            string batchName,
            DateTime batchDate,
            string createdBy,
            int batchSize,
            bool requireFullBatch,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(
                    batchName))
            {
                throw new InvalidOperationException(
                    "Section 49 batch name is required.");
            }

            if (string.IsNullOrWhiteSpace(
                    createdBy))
            {
                throw new InvalidOperationException(
                    "CreatedBy is required for Section 49 batch creation.");
            }

            if (batchSize <= 0)
            {
                throw new InvalidOperationException(
                    "Section 49 batch size must be greater than zero.");
            }

            var result =
                new List<S49BatchPickRow>();

            await using var cn =
                NoticeDb();

            await cn.OpenAsync(ct);

            await using var cmd =
                new SqlCommand(
                    "dbo.S49_Step3_AssignBatch",
                    cn)
                {
                    CommandType =
                        CommandType.StoredProcedure,

                    // Stored procedure is responsible for doing the heavy DB work.
                    CommandTimeout =
                        300
                };

            cmd.Parameters.Add(
                new SqlParameter(
                    "@RollId",
                    SqlDbType.Int)
                {
                    Value = rollId
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@BatchName",
                    SqlDbType.NVarChar,
                    100)
                {
                    Value = batchName.Trim()
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@BatchDate",
                    SqlDbType.Date)
                {
                    Value = batchDate.Date
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@CreatedBy",
                    SqlDbType.NVarChar,
                    150)
                {
                    Value = createdBy.Trim()
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@BatchSize",
                    SqlDbType.Int)
                {
                    Value = batchSize
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@RequireFullBatch",
                    SqlDbType.Bit)
                {
                    Value = requireFullBatch
                });

            await using var rd =
                await cmd.ExecuteReaderAsync(ct);

            while (await rd.ReadAsync(ct))
            {
                result.Add(
                    new S49BatchPickRow
                    {
                        PremiseId =
                            GetReaderString(
                                rd,
                                "PremiseId"),

                        RecipientEmail =
                            GetReaderString(
                                rd,
                                "RecipientEmail")
                    });
            }

            var validRows =
                result
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(
                            x.PremiseId))
                    .ToList();

            if (
                requireFullBatch
                &&
                validRows.Count != batchSize)
            {
                throw new InvalidOperationException(
                    $"Section 49 batch '{batchName}' requires exactly " +
                    $"{batchSize} records but the stored procedure returned " +
                    $"{validRows.Count}.");
            }

            if (validRows.Count == 0)
            {
                throw new InvalidOperationException(
                    "No Section 49 records were available for batching.");
            }

            return validRows;
        }

        // ============================================================
        // STATUS
        // ============================================================

        public Task MarkPrintingAsync(
            int rollId,
            string premiseId,
            CancellationToken ct)
        {
            return ExecPremiseStatusSpAsync(
                "dbo.S49_Step3_MarkPrinting",
                rollId,
                premiseId,
                ct);
        }

        public Task MarkPrintFailedAsync(
            int rollId,
            string premiseId,
            CancellationToken ct)
        {
            return ExecPremiseStatusSpAsync(
                "dbo.S49_Step3_MarkPrintFailed",
                rollId,
                premiseId,
                ct);
        }

        public Task MarkEmailSentAsync(
            int rollId,
            string premiseId,
            CancellationToken ct)
        {
            return ExecPremiseStatusSpAsync(
                "dbo.S49_Step3_MarkEmailSent",
                rollId,
                premiseId,
                ct);
        }

        public Task MarkEmailFailedAsync(
            int rollId,
            string premiseId,
            CancellationToken ct)
        {
            return ExecPremiseStatusSpAsync(
                "dbo.S49_Step3_MarkEmailFailed",
                rollId,
                premiseId,
                ct);
        }

        public async Task MarkPrintedAsync(
            int rollId,
            string premiseId,
            string batchName,
            string pdfPath,
            CancellationToken ct)
        {
            await using var cn =
                NoticeDb();

            await cn.OpenAsync(ct);

            await using var cmd =
                new SqlCommand(
                    "dbo.S49_Step3_MarkPrinted",
                    cn)
                {
                    CommandType =
                        CommandType.StoredProcedure,

                    CommandTimeout =
                        60
                };

            cmd.Parameters.Add(
                new SqlParameter(
                    "@RollId",
                    SqlDbType.Int)
                {
                    Value = rollId
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@PremiseId",
                    SqlDbType.VarChar,
                    50)
                {
                    Value = premiseId
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@BatchName",
                    SqlDbType.NVarChar,
                    100)
                {
                    Value = batchName
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@PdfPath",
                    SqlDbType.NVarChar,
                    2000)
                {
                    Value = pdfPath
                });

            await cmd.ExecuteNonQueryAsync(ct);
        }

        private async Task ExecPremiseStatusSpAsync(
            string storedProcedure,
            int rollId,
            string premiseId,
            CancellationToken ct)
        {
            await using var cn =
                NoticeDb();

            await cn.OpenAsync(ct);

            await using var cmd =
                new SqlCommand(
                    storedProcedure,
                    cn)
                {
                    CommandType =
                        CommandType.StoredProcedure,

                    CommandTimeout =
                        60
                };

            cmd.Parameters.Add(
                new SqlParameter(
                    "@RollId",
                    SqlDbType.Int)
                {
                    Value = rollId
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@PremiseId",
                    SqlDbType.VarChar,
                    50)
                {
                    Value = premiseId
                });

            await cmd.ExecuteNonQueryAsync(ct);
        }

        // ============================================================
        // MAPPERS
        // ============================================================

        private static S49RollRowDto MapRollRow(
            SqlDataReader rd,
            string premiseId)
        {
            return new S49RollRowDto
            {
                PremiseId =
                    GetReaderString(
                        rd,
                        "PremiseId")
                    ?? premiseId,

                PropertyDesc =
                    GetReaderString(
                        rd,
                        "PropertyDesc"),

                LisStreetAddress =
                    GetReaderStringSafe(
                        rd,
                        "LisStreetAddress"),

                CatDesc =
                    GetReaderString(
                        rd,
                        "CatDesc"),

                MarketValue =
                    GetReaderDecimal(
                        rd,
                        "MarketValue"),

                Extent =
                    GetReaderDecimalSafe(
                        rd,
                        "Extent"),

                ExtentText =
                    GetReaderStringSafe(
                        rd,
                        "Extent"),

                Reason =
                    GetReaderStringSafe(
                        rd,
                        "Reason"),

                EmailSent =
                    ParseEmailSent(
                        GetReaderStringSafe(
                            rd,
                            "EmailSent")),

                ValuationSplitIndicator =
                    GetReaderStringSafe(
                        rd,
                        "ValuationSplitIndicator"),

                ValuationKey =
                    GetReaderStringSafe(
                        rd,
                        "ValuationKey"),

                WEFDate =
                    GetReaderDateTimeSafe(
                        rd,
                        "WefDate")
            };
        }

        private static SapContactDto MapContact(
            SqlDataReader rd,
            string premiseId)
        {
            return new SapContactDto
            {
                PremiseId =
                    GetReaderString(
                        rd,
                        "PremiseId")
                    ?? premiseId,

                Email =
                    GetReaderStringSafe(
                        rd,
                        "Email"),

                Addr1 =
                    GetReaderStringSafe(
                        rd,
                        "ADDR1"),

                Addr2 =
                    GetReaderStringSafe(
                        rd,
                        "ADDR2"),

                Addr3 =
                    GetReaderStringSafe(
                        rd,
                        "ADDR3"),

                Addr4 =
                    GetReaderStringSafe(
                        rd,
                        "ADDR4"),

                Addr5 =
                    GetReaderStringSafe(
                        rd,
                        "ADDR5"),

                PremiseAddress =
                    GetReaderStringSafe(
                        rd,
                        "PremiseAddress"),

                AccountNo =
                    GetReaderStringSafe(
                        rd,
                        "AccountNo")
            };
        }

        // ============================================================
        // READER HELPERS
        // ============================================================

        private static bool HasColumn(
            SqlDataReader reader,
            string columnName)
        {
            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(
                        reader.GetName(i),
                        columnName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string? GetReaderString(
            SqlDataReader reader,
            string columnName)
        {
            var ordinal =
                reader.GetOrdinal(
                    columnName);

            return reader.IsDBNull(
                    ordinal)
                ? null
                : reader.GetValue(
                        ordinal)
                    ?.ToString()
                    ?.Trim();
        }

        private static string? GetReaderStringSafe(
            SqlDataReader reader,
            string columnName)
        {
            if (!HasColumn(
                    reader,
                    columnName))
            {
                return null;
            }

            return GetReaderString(
                reader,
                columnName);
        }

        private static decimal GetReaderDecimal(
            SqlDataReader reader,
            string columnName)
        {
            var ordinal =
                reader.GetOrdinal(
                    columnName);

            if (reader.IsDBNull(
                    ordinal))
            {
                return 0m;
            }

            var value =
                reader.GetValue(
                    ordinal);

            if (value is decimal d)
                return d;

            return decimal.TryParse(
                value?.ToString(),
                out var parsed)
                    ? parsed
                    : 0m;
        }

        private static decimal GetReaderDecimalSafe(
            SqlDataReader reader,
            string columnName)
        {
            return HasColumn(
                       reader,
                       columnName)
                ? GetReaderDecimal(
                    reader,
                    columnName)
                : 0m;
        }

        private static DateTime? GetReaderDateTimeSafe(
            SqlDataReader reader,
            string columnName)
        {
            if (!HasColumn(
                    reader,
                    columnName))
            {
                return null;
            }

            var ordinal =
                reader.GetOrdinal(
                    columnName);

            if (reader.IsDBNull(
                    ordinal))
            {
                return null;
            }

            var value =
                reader.GetValue(
                    ordinal);

            if (value is DateTime dt)
                return dt;

            return DateTime.TryParse(
                value?.ToString(),
                out var parsed)
                    ? parsed
                    : null;
        }

        private static int ParseEmailSent(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return 0;
            }

            if (int.TryParse(
                    value,
                    out var number))
            {
                return number;
            }

            return value.Trim().Equals(
                       "No",
                       StringComparison.OrdinalIgnoreCase)
                   ||
                   value.Trim().Equals(
                       "N",
                       StringComparison.OrdinalIgnoreCase)
                ? 0
                : 1;
        }
    }
}
