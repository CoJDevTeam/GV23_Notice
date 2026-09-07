using GV23_Notice.Data;
using GV23_Notice.Domain.Rolls;
using GV23_Notice.Models.DTOs;
using GV23_Notice.Models.DTOs.GV23_Notice.Models.DTOs;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Data;

namespace GV23_Notice.Services.Rolls
{
    public sealed class S49RollRepository : IS49RollRepository
    {
      

        private readonly IConfiguration _cfg;
        private readonly IWorkflowRollResolver _rollResolver;
        private readonly IRollDbConnectionFactory _connectionFactory;
        private readonly RollDbOptions _rollDb;

        public S49RollRepository(
            IConfiguration cfg,
            IWorkflowRollResolver rollResolver,
            IRollDbConnectionFactory connectionFactory,
            IOptions<RollDbOptions> rollDbOptions)
        {
            _cfg = cfg;
            _rollResolver = rollResolver;
            _connectionFactory = connectionFactory;
            _rollDb = rollDbOptions.Value;
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
        // RESOLVE ROLL
        // ============================================================

        private async Task<ResolvedS49Roll> ResolveAsync(
     int rollId,
     CancellationToken ct)
        {
            // ---------------------------------------------------------
            // RollRegistry is resolved by the shared roll resolver.
            // ---------------------------------------------------------
            var roll =
                await _rollResolver.ResolveByRollIdAsync(
                    rollId,
                    ct);

            if (string.IsNullOrWhiteSpace(
                    roll.SourceDb))
            {
                throw new InvalidOperationException(
                    $"RollRegistry.SourceDb is missing for RollId {rollId}.");
            }

            var sourceDb =
                roll.SourceDb.Trim();

            // ---------------------------------------------------------
            // All roll-specific settings come from:
            //
            // RollDb:Sources:{SourceDb}
            // ---------------------------------------------------------
            var source =
                _rollDb.GetSource(
                    sourceDb);

            if (string.IsNullOrWhiteSpace(
                    source.RollTable))
            {
                throw new InvalidOperationException(
                    $"RollTable is not configured for '{sourceDb}'.");
            }

            if (string.IsNullOrWhiteSpace(
                    source.ContactTable))
            {
                throw new InvalidOperationException(
                    $"ContactTable is not configured for '{sourceDb}'.");
            }

            return new ResolvedS49Roll
            {
                RollId =
                    roll.RollId,

                ShortCode =
                    roll.ShortCode?.Trim()
                    ?? string.Empty,

                SourceDb =
                    sourceDb,

                RollTable =
                    source.RollTable.Trim(),

                ContactTable =
                    source.ContactTable.Trim(),

                Section49 =
                    source.Section49
                    ?? new RollSection49Options()
            };
        }

        // ============================================================
        // PICK NEXT PREMISES
        // ============================================================

        public async Task<List<string>>
            PickNextPremiseIdsAsync(
                int rollId,
                int top,
                CancellationToken ct)
        {
            var resolved =
                await ResolveAsync(
                    rollId,
                    ct);

            if (top <= 0)
                top = 500;

            if (top > 500)
                top = 500;

            var sql =
                BuildPickPremiseSql(
                    resolved);

            var list =
                new List<string>();

            await using var cn =
     _connectionFactory.Create(
         resolved.SourceDb);

            await cn.OpenAsync(ct);

            await using var cmd =
                new SqlCommand(sql, cn)
                {
                    CommandTimeout = 60
                };

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
                    rd.GetValue(0)
                        ?.ToString()
                        ?.Trim();

                if (!string.IsNullOrWhiteSpace(
                        premiseId))
                {
                    list.Add(
                        premiseId);
                }
            }

            return list;
        }

        private static string BuildPickPremiseSql(
            ResolvedS49Roll resolved)
        {
            var db =
                QuoteSqlIdentifier(
                    resolved.SourceDb);

            var rollTable =
                QuoteSqlIdentifier(
                    resolved.RollTable);

            var contactTable =
                QuoteSqlIdentifier(
                    resolved.ContactTable);

            // ========================================================
            // NEW ROLLS:
            // Email_Sent means:
            // Yes = email exists
            // No  = no email
            //
            // Processing state belongs in Section49Table.
            // ========================================================

            if (IsEmailAvailabilityMode(
                    resolved.Section49))
            {
                return $"""
                    SELECT TOP (@Top)
                        r.PREMISEID
                    FROM {db}.dbo.{rollTable} r

                    WHERE
                        (
                            r.Batch_Name IS NULL
                            OR
                            LTRIM(RTRIM(r.Batch_Name)) = ''
                        )

                        AND NULLIF(
                            LTRIM(RTRIM(r.PREMISEID)),
                            ''
                        ) IS NOT NULL

                    GROUP BY
                        r.PREMISEID

                    ORDER BY
                        MIN(r.Id) ASC;
                    """;
            }

            // ========================================================
            // LEGACY GV23 / SUP1 / SUP2 / SUP3
            // Preserve current behaviour exactly.
            // ========================================================

            return $"""
                SELECT TOP (@Top)
                    r.PREMISEID

                FROM {db}.dbo.{rollTable} r

                INNER JOIN {db}.dbo.{contactTable} c
                    ON c.PREMISE_ID = r.PREMISEID

                WHERE
                    (
                           r.Email_Sent IS NULL
                        OR CAST(r.Email_Sent AS VARCHAR(10)) = '0'
                        OR CAST(r.Email_Sent AS VARCHAR(10))
                           NOT IN ('P', 'Y', 'N', 'NP')
                    )

                    AND r.Batch_Name IS NULL

                    AND NULLIF(
                        LTRIM(RTRIM(c.EMAIL_ADDR)),
                        ''
                    ) IS NOT NULL

                GROUP BY
                    r.PREMISEID

                ORDER BY
                    MIN(r.Id) ASC;
                """;
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
            var resolved =
                await ResolveAsync(
                    rollId,
                    ct);

            /*
             * Keep the existing Notice_DB stored procedure for
             * legacy rolls.
             *
             * New dynamic rolls are loaded directly using their
             * configured RollTable / ContactTable.
             */
            if (IsEmailAvailabilityMode(
                    resolved.Section49))
            {
                return await LoadPremiseDynamicAsync(
                    resolved,
                    premiseId,
                    ct);
            }

            return await LoadPremiseLegacyAsync(
                rollId,
                premiseId,
                ct);
        }

        private async Task<
            (
                List<S49RollRowDto> rows,
                SapContactDto? contact
            )>
            LoadPremiseLegacyAsync(
                int rollId,
                string premiseId,
                CancellationToken ct)
        {
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

                    CommandTimeout = 60
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

            await using var rd =
                await cmd.ExecuteReaderAsync(ct);

            while (await rd.ReadAsync(ct))
            {
                rows.Add(
                    MapRollRow(
                        rd,
                        premiseId));
            }

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

            return (
                rows,
                contact);
        }

        // ============================================================
        // DYNAMIC LOAD
        // ============================================================

        private async Task<
            (
                List<S49RollRowDto> rows,
                SapContactDto? contact
            )>
            LoadPremiseDynamicAsync(
                ResolvedS49Roll resolved,
                string premiseId,
                CancellationToken ct)
        {
            var rows =
                new List<S49RollRowDto>();

            SapContactDto? contact =
                null;

            var rollTable =
                QuoteSqlIdentifier(
                    resolved.RollTable);

            var contactTable =
                QuoteSqlIdentifier(
                    resolved.ContactTable);

            var sql = $"""
                SELECT
                    r.PREMISEID
                        AS PremiseId,

                    r.PropertyDesc
                        AS PropertyDesc,

                    r.LISStreetAddress
                        AS LisStreetAddress,

                    r.CatDesc
                        AS CatDesc,

                    TRY_CONVERT(
                        DECIMAL(18,2),
                        r.MarketValue
                    ) AS MarketValue,

                    TRY_CONVERT(
                        DECIMAL(18,4),
                        r.Extent
                    ) AS Extent,

                    r.Reason
                        AS Reason,

                    r.Email_Sent
                        AS EmailSent,

                    r.WefDate
                        AS WefDate

                FROM dbo.{rollTable} r

                WHERE
                    LTRIM(RTRIM(r.PREMISEID))
                        =
                    LTRIM(RTRIM(@PremiseId))

                ORDER BY
                    r.Id;


                SELECT TOP 1

                    c.PREMISE_ID
                        AS PremiseId,

                    c.EMAIL_ADDR
                        AS Email,

                    c.ADDR1,
                    c.ADDR2,
                    c.ADDR3,
                    c.ADDR4,
                    c.ADDR5,

                    c.PREMISE_ADDRESS
                        AS PremiseAddress,

                    CAST(NULL AS VARCHAR(100))
                        AS AccountNo

                FROM dbo.{contactTable} c

                WHERE
                    LTRIM(RTRIM(c.PREMISE_ID))
                        =
                    LTRIM(RTRIM(@PremiseId))

                ORDER BY
                    CASE
                        WHEN NULLIF(
                            LTRIM(RTRIM(c.EMAIL_ADDR)),
                            ''
                        ) IS NOT NULL
                        THEN 0
                        ELSE 1
                    END,
                    c.ID DESC;
                """;

            await using var cn =
         _connectionFactory.Create(
             resolved.SourceDb);

            await cn.OpenAsync(ct);

            await using var cmd =
                new SqlCommand(sql, cn)
                {
                    CommandTimeout = 60
                };

            cmd.Parameters.Add(
                new SqlParameter(
                    "@PremiseId",
                    SqlDbType.VarChar,
                    50)
                {
                    Value = premiseId
                });

            await using var rd =
                await cmd.ExecuteReaderAsync(ct);

            while (await rd.ReadAsync(ct))
            {
                rows.Add(
                    MapRollRow(
                        rd,
                        premiseId));
            }

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

            return (
                rows,
                contact);
        }

        // ============================================================
        // STATUS UPDATES
        // ============================================================

        public async Task MarkPrintingAsync(
            int rollId,
            string premiseId,
            CancellationToken ct)
        {
            var resolved =
                await ResolveAsync(
                    rollId,
                    ct);

            if (IsEmailAvailabilityMode(
                    resolved.Section49))
            {
                await UpdateAuditStatusAsync(
                    resolved,
                    premiseId,
                    "Printing",
                    null,
                    ct);

                return;
            }

            await ExecStatusSpAsync(
                "dbo.S49_Step3_MarkPrinting",
                rollId,
                premiseId,
                ct);
        }

        public async Task MarkPrintFailedAsync(
            int rollId,
            string premiseId,
            CancellationToken ct)
        {
            var resolved =
                await ResolveAsync(
                    rollId,
                    ct);

            if (IsEmailAvailabilityMode(
                    resolved.Section49))
            {
                await UpdateAuditStatusAsync(
                    resolved,
                    premiseId,
                    "Failed",
                    "PDF generation failed.",
                    ct);

                return;
            }

            await ExecStatusSpAsync(
                "dbo.S49_Step3_MarkPrintFailed",
                rollId,
                premiseId,
                ct);
        }

        public async Task MarkEmailSentAsync(
            int rollId,
            string premiseId,
            CancellationToken ct)
        {
            var resolved =
                await ResolveAsync(
                    rollId,
                    ct);

            if (IsEmailAvailabilityMode(
                    resolved.Section49))
            {
                await UpdateAuditStatusAsync(
                    resolved,
                    premiseId,
                    "Sent",
                    null,
                    ct);

                return;
            }

            await ExecStatusSpAsync(
                "dbo.S49_Step3_MarkEmailSent",
                rollId,
                premiseId,
                ct);
        }

        public async Task MarkEmailFailedAsync(
            int rollId,
            string premiseId,
            CancellationToken ct)
        {
            var resolved =
                await ResolveAsync(
                    rollId,
                    ct);

            if (IsEmailAvailabilityMode(
                    resolved.Section49))
            {
                await UpdateAuditStatusAsync(
                    resolved,
                    premiseId,
                    "Failed",
                    "Email send failed.",
                    ct);

                return;
            }

            await ExecStatusSpAsync(
                "dbo.S49_Step3_MarkEmailFailed",
                rollId,
                premiseId,
                ct);
        }

        // ============================================================
        // NEW-ROLL AUDIT STATUS
        // ============================================================

        private async Task UpdateAuditStatusAsync(
     ResolvedS49Roll resolved,
     string premiseId,
     string status,
     string? error,
     CancellationToken ct)
        {
            if (!resolved.Section49.HasAuditTable)
            {
                throw new InvalidOperationException(
                    $"Section 49 audit table is not configured for '{resolved.SourceDb}'.");
            }

            var auditTable =
                QuoteSqlIdentifier(
                    resolved.Section49.AuditTable);

            var sql = $"""
        UPDATE dbo.{auditTable}
        SET
            Send_Status = @Status,
            Error_Message = @Error
        WHERE
            PREMISE_ID = @PremiseId;
        """;

            await using var cn =
                _connectionFactory.Create(
                    resolved.SourceDb);

            await cn.OpenAsync(ct);

            await using var cmd =
                new SqlCommand(
                    sql,
                    cn)
                {
                    CommandTimeout = 30
                };

            cmd.Parameters.Add(
                new SqlParameter(
                    "@Status",
                    SqlDbType.NVarChar,
                    50)
                {
                    Value = status
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@Error",
                    SqlDbType.NVarChar,
                    2000)
                {
                    Value =
                        string.IsNullOrWhiteSpace(error)
                            ? DBNull.Value
                            : error
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
        // LEGACY STATUS STORED PROCEDURES
        // ============================================================

        private async Task ExecStatusSpAsync(
            string spName,
            int rollId,
            string premiseId,
            CancellationToken ct)
        {
            await using var cn =
                NoticeDb();

            await cn.OpenAsync(ct);

            await using var cmd =
                new SqlCommand(
                    spName,
                    cn)
                {
                    CommandType =
                        CommandType.StoredProcedure,

                    CommandTimeout = 30
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
                    GetString(
                        rd,
                        "PremiseId")
                    ?? premiseId,

                PropertyDesc =
                    GetString(
                        rd,
                        "PropertyDesc"),

                LisStreetAddress =
                    GetString(
                        rd,
                        "LisStreetAddress"),

                CatDesc =
                    GetString(
                        rd,
                        "CatDesc"),

                MarketValue =
                    GetDecimal(
                        rd,
                        "MarketValue"),

                Extent =
                    GetDecimal(
                        rd,
                        "Extent"),

                Reason =
                    GetString(
                        rd,
                        "Reason"),

                EmailSent =
                    ParseLegacyEmailSent(
                        GetString(
                            rd,
                            "EmailSent")),

                WEFDate =
                    GetNullableDateTime(
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
                    GetString(
                        rd,
                        "PremiseId")
                    ?? premiseId,

                Email =
                    GetString(
                        rd,
                        "Email"),

                Addr1 =
                    GetString(
                        rd,
                        "ADDR1"),

                Addr2 =
                    GetString(
                        rd,
                        "ADDR2"),

                Addr3 =
                    GetString(
                        rd,
                        "ADDR3"),

                Addr4 =
                    GetString(
                        rd,
                        "ADDR4"),

                Addr5 =
                    GetString(
                        rd,
                        "ADDR5"),

                PremiseAddress =
                    GetString(
                        rd,
                        "PremiseAddress"),

                AccountNo =
                    GetString(
                        rd,
                        "AccountNo")
            };
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private static bool IsEmailAvailabilityMode(
            RollSection49Options options)
        {
            return string.Equals(
                options.EmailSentMode,
                RollSection49EmailSentModes.EmailAvailability,
                StringComparison.OrdinalIgnoreCase);
        }

        private static int ParseLegacyEmailSent(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            if (int.TryParse(
                    value,
                    out var number))
            {
                return number;
            }

            /*
             * Existing DTO uses int.
             * For Y/P/N/NP/Yes/No simply indicate
             * that the source value exists.
             */
            return 1;
        }

        private static string? GetString(
            SqlDataReader reader,
            string name)
        {
            var ordinal =
                reader.GetOrdinal(name);

            return reader.IsDBNull(ordinal)
                ? null
                : reader.GetValue(ordinal)
                    ?.ToString()
                    ?.Trim();
        }

        private static decimal GetDecimal(
            SqlDataReader reader,
            string name)
        {
            var ordinal =
                reader.GetOrdinal(name);

            if (reader.IsDBNull(ordinal))
                return 0m;

            return Convert.ToDecimal(
                reader.GetValue(ordinal));
        }

        private static DateTime? GetNullableDateTime(
            SqlDataReader reader,
            string name)
        {
            var ordinal =
                reader.GetOrdinal(name);

            if (reader.IsDBNull(ordinal))
                return null;

            return Convert.ToDateTime(
                reader.GetValue(ordinal));
        }

        private static string QuoteSqlIdentifier(
            string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    "SQL identifier cannot be empty.");
            }

            return $"[{value.Replace("]", "]]")}]";
        }
        public async Task<List<S49BatchPickRow>> AssignBatchAsync(
    int rollId,
    string batchName,
    DateTime batchDate,
    string createdBy,
    int batchSize,
    bool requireFullBatch,
    CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(batchName))
            {
                throw new InvalidOperationException(
                    "Section 49 batch name is required.");
            }

            if (string.IsNullOrWhiteSpace(createdBy))
            {
                throw new InvalidOperationException(
                    "CreatedBy is required for Section 49 batch creation.");
            }

            if (batchSize <= 0)
            {
                throw new InvalidOperationException(
                    "Section 49 batch size must be greater than zero.");
            }

            var resolved =
                await ResolveAsync(
                    rollId,
                    ct);

            // ---------------------------------------------------------
            // Existing rolls:
            // GV23 / SUPP1 / SUPP2 / SUPP3
            // Keep using the existing Notice_DB stored procedure.
            // ---------------------------------------------------------
            if (!IsEmailAvailabilityMode(
                    resolved.Section49))
            {
                return await AssignLegacyBatchAsync(
                    rollId,
                    batchName,
                    batchDate,
                    batchSize,
                    requireFullBatch,
                    ct);
            }

            // ---------------------------------------------------------
            // New dynamic standard:
            // SUPP4 and future configured rolls.
            // ---------------------------------------------------------
            return await AssignDynamicBatchAsync(
                resolved,
                batchName,
                batchDate,
                createdBy,
                batchSize,
                requireFullBatch,
                ct);
        }
        private async Task<List<S49BatchPickRow>>
    AssignLegacyBatchAsync(
        int rollId,
        string batchName,
        DateTime batchDate,
        int batchSize,
        bool requireFullBatch,
        CancellationToken ct)
        {
            var rows =
                new List<S49BatchPickRow>();

            await using var cn =
                NoticeDb();

            await cn.OpenAsync(ct);

            await using var cmd =
                new SqlCommand(
                    "dbo.S49_Step3_AssignTop500ToBatch",
                    cn)
                {
                    CommandType =
                        CommandType.StoredProcedure,

                    CommandTimeout = 180
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
                    Value = batchName
                });

            cmd.Parameters.Add(
                new SqlParameter(
                    "@BatchDate",
                    SqlDbType.Date)
                {
                    Value = batchDate.Date
                });

            await using var rd =
                await cmd.ExecuteReaderAsync(ct);

            while (await rd.ReadAsync(ct))
            {
                rows.Add(
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

            rows =
                rows
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(
                            x.PremiseId))
                    .ToList();

            if (
                requireFullBatch
                &&
                rows.Count != batchSize)
            {
                throw new InvalidOperationException(
                    $"Section 49 batch '{batchName}' requires exactly " +
                    $"{batchSize} records but the legacy procedure returned " +
                    $"{rows.Count}.");
            }

            return rows;
        }

        private async Task<List<S49BatchPickRow>>
    AssignDynamicBatchAsync(
        ResolvedS49Roll resolved,
        string batchName,
        DateTime batchDate,
        string createdBy,
        int batchSize,
        bool requireFullBatch,
        CancellationToken ct)
        {
            if (!resolved.Section49.HasAuditTable)
            {
                throw new InvalidOperationException(
                    $"Section 49 AuditTable is not configured for " +
                    $"'{resolved.SourceDb}'.");
            }

            var rollTable =
                QuoteSqlIdentifier(
                    resolved.RollTable);

            var contactTable =
                QuoteSqlIdentifier(
                    resolved.ContactTable);

            var auditTable =
                QuoteSqlIdentifier(
                    resolved.Section49.AuditTable);

            var result =
                new List<S49BatchPickRow>();

            await using var cn =
                _connectionFactory.Create(
                    resolved.SourceDb);

            await cn.OpenAsync(ct);

            await using var transaction =
                (SqlTransaction)await cn.BeginTransactionAsync(ct);

            try
            {
                var sql = $"""
            SET NOCOUNT ON;
            SET XACT_ABORT ON;

            ------------------------------------------------------------
            -- Prevent duplicate batch name
            ------------------------------------------------------------
            IF EXISTS
            (
                SELECT 1
                FROM dbo.{auditTable}
                WHERE Batch_Name = @BatchName
            )
            BEGIN
                THROW 51001,
                    'Section 49 batch name already exists.',
                    1;
            END;


            ------------------------------------------------------------
            -- Freeze the selected premises.
            ------------------------------------------------------------
            CREATE TABLE #Picked
            (
                PREMISE_ID VARCHAR(50) NOT NULL PRIMARY KEY,
                FirstId BIGINT NULL
            );


            INSERT INTO #Picked
            (
                PREMISE_ID,
                FirstId
            )
            SELECT TOP (@BatchSize)

                LTRIM(RTRIM(r.PREMISEID))
                    AS PREMISE_ID,

                MIN(r.Id)
                    AS FirstId

            FROM dbo.{rollTable} r
                WITH
                (
                    UPDLOCK,
                    READPAST,
                    ROWLOCK
                )

            WHERE
                NULLIF(
                    LTRIM(RTRIM(r.PREMISEID)),
                    ''
                ) IS NOT NULL

                AND
                (
                    r.Batch_Name IS NULL
                    OR
                    LTRIM(RTRIM(r.Batch_Name)) = ''
                )

            GROUP BY
                LTRIM(RTRIM(r.PREMISEID))

            ORDER BY
                MIN(r.Id);


            ------------------------------------------------------------
            -- Validate selected count.
            ------------------------------------------------------------
            DECLARE @SelectedCount INT;

            SELECT
                @SelectedCount = COUNT(*)
            FROM #Picked;


            IF
            (
                @RequireFullBatch = 1
                AND
                @SelectedCount <> @BatchSize
            )
            BEGIN
                DECLARE @ErrorMessage NVARCHAR(500);

                SET @ErrorMessage =
                    CONCAT(
                        'Section 49 batch requires exactly ',
                        @BatchSize,
                        ' records. Only ',
                        @SelectedCount,
                        ' records were available.'
                    );

                THROW 51002,
                    @ErrorMessage,
                    1;
            END;


            IF @SelectedCount = 0
            BEGIN
                THROW 51003,
                    'No Section 49 records are available for batching.',
                    1;
            END;


            ------------------------------------------------------------
            -- Create permanent Section 49 audit snapshot.
            ------------------------------------------------------------
            INSERT INTO dbo.{auditTable}
            (
                PREMISE_ID,
                Property_Desc,

                ADDR1,
                ADDR2,
                ADDR3,
                ADDR4,
                ADDR5,

                EMAIL_ADDR,
                Has_Email,

                Batch_Name,
                Batch_Date,

                Created_By,

                Original_Email_Addr,

                Is_Test_Mode,
                Send_Status
            )

            SELECT
                p.PREMISE_ID,

                ISNULL(
                    NULLIF(
                        LTRIM(RTRIM(RollRow.PropertyDesc)),
                        ''
                    ),
                    p.PREMISE_ID
                ) AS Property_Desc,

                ContactRow.ADDR1,
                ContactRow.ADDR2,
                ContactRow.ADDR3,
                ContactRow.ADDR4,
                ContactRow.ADDR5,

                NULLIF(
                    LTRIM(RTRIM(ContactRow.EMAIL_ADDR)),
                    ''
                ) AS EMAIL_ADDR,

                CASE
                    WHEN NULLIF(
                        LTRIM(RTRIM(ContactRow.EMAIL_ADDR)),
                        ''
                    ) IS NULL
                    THEN 'No'
                    ELSE 'Yes'
                END AS Has_Email,

                @BatchName,
                @BatchDate,

                @CreatedBy,

                NULLIF(
                    LTRIM(RTRIM(ContactRow.EMAIL_ADDR)),
                    ''
                ) AS Original_Email_Addr,

                0 AS Is_Test_Mode,

                'Pending'
                    AS Send_Status

            FROM #Picked p

            OUTER APPLY
            (
                SELECT TOP 1
                    r.PropertyDesc
                FROM dbo.{rollTable} r
                WHERE
                    LTRIM(RTRIM(r.PREMISEID))
                    =
                    p.PREMISE_ID
                ORDER BY
                    r.Id
            ) RollRow

            OUTER APPLY
            (
                SELECT TOP 1
                    c.EMAIL_ADDR,
                    c.ADDR1,
                    c.ADDR2,
                    c.ADDR3,
                    c.ADDR4,
                    c.ADDR5

                FROM dbo.{contactTable} c

                WHERE
                    LTRIM(RTRIM(c.PREMISE_ID))
                    =
                    p.PREMISE_ID

                ORDER BY
                    CASE
                        WHEN NULLIF(
                            LTRIM(RTRIM(c.EMAIL_ADDR)),
                            ''
                        ) IS NOT NULL
                        THEN 0
                        ELSE 1
                    END,

                    c.ID DESC
            ) ContactRow;


            ------------------------------------------------------------
            -- Stamp the source roll.
            --
            -- IMPORTANT:
            -- Email_Sent is availability only for these configured
            -- rolls:
            --
            -- Yes = an email address exists
            -- No  = no email address exists
            ------------------------------------------------------------
            UPDATE r

            SET
                r.Batch_Name =
                    @BatchName,

                r.Batch_Date =
                    @BatchDate,

                r.Email_Sent =
                    CASE
                        WHEN NULLIF(
                            LTRIM(RTRIM(ContactRow.EMAIL_ADDR)),
                            ''
                        ) IS NULL
                        THEN 'No'
                        ELSE 'Yes'
                    END

            FROM dbo.{rollTable} r

            INNER JOIN #Picked p
                ON
                    LTRIM(RTRIM(r.PREMISEID))
                    =
                    p.PREMISE_ID

            OUTER APPLY
            (
                SELECT TOP 1
                    c.EMAIL_ADDR

                FROM dbo.{contactTable} c

                WHERE
                    LTRIM(RTRIM(c.PREMISE_ID))
                    =
                    p.PREMISE_ID

                ORDER BY
                    CASE
                        WHEN NULLIF(
                            LTRIM(RTRIM(c.EMAIL_ADDR)),
                            ''
                        ) IS NOT NULL
                        THEN 0
                        ELSE 1
                    END,

                    c.ID DESC
            ) ContactRow;


            ------------------------------------------------------------
            -- Return the exact locked batch.
            ------------------------------------------------------------
            SELECT
                s.PREMISE_ID
                    AS PremiseId,

                s.EMAIL_ADDR
                    AS RecipientEmail

            FROM dbo.{auditTable} s

            WHERE
                s.Batch_Name =
                    @BatchName

            ORDER BY
                s.Id;
            """;

                await using var cmd =
                    new SqlCommand(
                        sql,
                        cn,
                        transaction)
                    {
                        CommandTimeout = 180
                    };

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
                        Value = createdBy
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

                await rd.CloseAsync();

                var validCount =
                    result.Count(x =>
                        !string.IsNullOrWhiteSpace(
                            x.PremiseId));

                if (
                    requireFullBatch
                    &&
                    validCount != batchSize)
                {
                    throw new InvalidOperationException(
                        $"Section 49 batch '{batchName}' expected exactly " +
                        $"{batchSize} records but {validCount} were created.");
                }

                await transaction.CommitAsync(ct);

                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
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

        // ============================================================
        // PRIVATE RESOLVED MODEL
        // ============================================================

        private sealed class ResolvedS49Roll
        {
            public int RollId { get; init; }

            public string ShortCode { get; init; } =
                string.Empty;

            public string SourceDb { get; init; } =
                string.Empty;

            public string RollTable { get; init; } =
                string.Empty;

            public string ContactTable { get; init; } =
                string.Empty;

            public RollSection49Options Section49 { get; init; } =
                new();
        }
    }
}