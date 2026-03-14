using GV23_Notice.Data;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models.DTOs;
using GV23_Notice.Models.Workflow.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace GV23_Notice.Services.Step3
{
    public sealed class Step3BatchService : IStep3BatchService
    {
        private readonly AppDbContext _db;

        public Step3BatchService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<int> CreateBatchAsync(Guid workflowKey, DateTime batchDate, string createdBy, CancellationToken ct)
        {
            // 1) Resolve settings
            var s = await _db.NoticeSettings
                        .FirstOrDefaultAsync(x => x.ApprovalKey == workflowKey, ct)
                    ?? await _db.NoticeSettings
                        .FirstOrDefaultAsync(x => x.WorkflowKey == workflowKey, ct)
                    ?? throw new InvalidOperationException("Workflow not found.");

            if (!s.IsApproved)
                throw new InvalidOperationException("Step 1 must be approved before creating Step 3 batch.");

            var roll = await _db.RollRegistry.AsNoTracking()
                           .FirstOrDefaultAsync(r => r.RollId == s.RollId, ct)
                       ?? throw new InvalidOperationException("Roll not found.");

            // 2) Compute correct prefix per notice type
            var shortCode = roll.ShortCode ?? "";
            var prefix = s.Notice switch
            {
                NoticeKind.S52 => (s.IsSection52Review ? "S52" : "AD") + $"_{shortCode}_",
                NoticeKind.DJ => $"DJ_{shortCode}_",
                NoticeKind.IN => ((s.IsInvalidOmission ?? false) ? "IOM" : "IOBJ") + $"_{shortCode}_",
                _ => $"{s.Notice}_{shortCode}_"
            };

            // 3) Compute next sequence using the correct prefix
            var last = await _db.NoticeBatches.AsNoTracking()
                .Where(b => b.RollId == s.RollId
                         && b.BatchKind == "STEP3"
                         && b.BatchName.StartsWith(prefix))
                .OrderByDescending(b => b.Id)
                .FirstOrDefaultAsync(ct);

            var nextSeq = 1;
            if (last != null && last.BatchName.Length > prefix.Length)
            {
                var suf = last.BatchName[prefix.Length..];
                if (int.TryParse(suf, out var parsed))
                    nextSeq = parsed + 1;
            }

            var batchName = $"{prefix}{nextSeq:0000}";
            var batchDateUtc = batchDate.Date;
            var nowUtc = DateTime.UtcNow;

            // 4) Insert batch header
            var batch = new NoticeBatch
            {
                NoticeSettingsId = s.Id,
                WorkflowKey = workflowKey,
                RollId = s.RollId,
                Mode = s.Mode,
                Notice = s.Notice,
                SettingsVersionUsed = s.Version,
                Version = s.Version.ToString(),
                BatchKind = "STEP3",
                BatchName = batchName,
                BatchDate = batchDateUtc,   // ✅ always set
                BulkFromDate = s.BulkFromDate,
                BulkToDate = s.BulkToDate,
                NumberOfRecords = 0,
                IsApproved = false,
                CreatedBy = createdBy,
                CreatedAtUtc = nowUtc
            };

            _db.NoticeBatches.Add(batch);
            await _db.SaveChangesAsync(ct);

            // 5) Notice-specific SP call — all now pass BatchName + BatchDate
            switch (s.Notice)
            {
                case NoticeKind.S49:
                    {
                        // SP: EXEC dbo.S49_Step3_AssignTop500ToBatch @RollId, @BatchName, @BatchDate
                        var picked = await _db.Set<S49BatchPickRow>()
                            .FromSqlRaw("EXEC dbo.S49_Step3_AssignTop500ToBatch @p0, @p1, @p2",
                                s.RollId, batchName, batchDateUtc)
                            .ToListAsync(ct);

                        var runLogs = picked
                            .Where(x => !string.IsNullOrWhiteSpace(x.PremiseId))
                            .Select(x => new NoticeRunLog
                            {
                                NoticeBatchId = batch.Id,
                                PremiseId = x.PremiseId,
                                RecipientEmail = x.RecipientEmail,
                                Status = RunStatus.Generated,
                                CreatedAtUtc = nowUtc
                            }).ToList();

                        _db.NoticeRunLogs.AddRange(runLogs);
                        batch.NumberOfRecords = runLogs.Count;
                        await _db.SaveChangesAsync(ct);
                        return batch.Id;
                    }

                case NoticeKind.S51:
                    {
                        var closingDate = (s.EvidenceCloseDate ?? s.LetterDate.AddDays(30)).Date;

                        // SP: EXEC dbo.S51_Step3_InsertTop500IntoSection51Table @RollId, @BatchName, @BatchDate, @ClosingDate
                        var picked = await _db.Set<S51BatchPickRow>()
                            .FromSqlRaw("EXEC dbo.S51_Step3_InsertTop500IntoSection51Table @p0, @p1, @p2, @p3",
                                s.RollId, batchName, batchDateUtc, closingDate)
                            .ToListAsync(ct);

                        var runLogs = picked
                            .Where(x => !string.IsNullOrWhiteSpace(x.ObjectionNo))
                            .Select(x => new NoticeRunLog
                            {
                                NoticeBatchId = batch.Id,
                                ObjectionNo = x.ObjectionNo,
                                PremiseId = x.PremiseId,
                                RecipientEmail = x.RecipientEmail,
                                Status = RunStatus.Generated,
                                CreatedAtUtc = nowUtc
                            }).ToList();

                        _db.NoticeRunLogs.AddRange(runLogs);
                        batch.NumberOfRecords = runLogs.Count;
                        await _db.SaveChangesAsync(ct);
                        return batch.Id;
                    }

                case NoticeKind.S52:
                    {
                        if (!s.BulkFromDate.HasValue || !s.BulkToDate.HasValue)
                            throw new InvalidOperationException("S52 batch requires Bulk From and Bulk To dates.");

                        // SP: EXEC dbo.S52_Step3_SelectTop500ByRange @RollId, @FromDate, @ToDate, @IsReview
                        var picked = await _db.Set<S52BatchPickRow>()
                            .FromSqlRaw("EXEC dbo.S52_Step3_SelectTop500ByRange @p0, @p1, @p2, @p3",
                                s.RollId,
                                s.BulkFromDate.Value.Date,
                                s.BulkToDate.Value.Date,
                                s.IsSection52Review)
                            .ToListAsync(ct);

                        var runLogs = picked
                            .Where(x => !string.IsNullOrWhiteSpace(x.AppealNo))
                            .Select(x => new NoticeRunLog
                            {
                                NoticeBatchId = batch.Id,
                                AppealNo = x.AppealNo,
                                ObjectionNo = x.ObjectionNo,
                                PremiseId = x.PremiseId,
                                RecipientEmail = x.RecipientEmail,
                                Status = RunStatus.Generated,
                                CreatedAtUtc = nowUtc
                            }).ToList();

                        _db.NoticeRunLogs.AddRange(runLogs);
                        batch.NumberOfRecords = runLogs.Count;
                        await _db.SaveChangesAsync(ct);
                        return batch.Id;
                    }

                case NoticeKind.DJ:
                    {
                        var connStr = _db.Database.GetConnectionString()
                                      ?? throw new InvalidOperationException("Connection string not found.");

                        var djRows = new List<DjBatchPickRow>();

                        using (var spConn = new Microsoft.Data.SqlClient.SqlConnection(connStr))
                        {
                            await spConn.OpenAsync(ct);

                            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                                "EXEC dbo.DJ_Step3_InsertTop500IntoDearJohnnyTable @RollId, @BatchName, @BatchDate, @LetterDate",
                                spConn)
                            {
                                CommandTimeout = 300
                            };

                            cmd.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@RollId", System.Data.SqlDbType.Int)
                            {
                                Value = s.RollId
                            });
                            cmd.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@BatchName", System.Data.SqlDbType.NVarChar, 100)
                            {
                                Value = batchName
                            });
                            cmd.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@BatchDate", System.Data.SqlDbType.DateTime2)
                            {
                                Value = batchDateUtc
                            });
                            cmd.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@LetterDate", System.Data.SqlDbType.DateTime2)
                            {
                                Value = s.LetterDate.Date
                            });

                            using var reader = await cmd.ExecuteReaderAsync(ct);
                            while (await reader.ReadAsync(ct))
                            {
                                static string? S(Microsoft.Data.SqlClient.SqlDataReader r, string col)
                                {
                                    var ordinal = r.GetOrdinal(col);
                                    return r.IsDBNull(ordinal) ? null : r.GetValue(ordinal)?.ToString()?.Trim();
                                }

                                djRows.Add(new DjBatchPickRow
                                {
                                    ObjectionNo = S(reader, "ObjectionNo"),
                                    PremiseId = S(reader, "PremiseId"),
                                    RecipientEmail = S(reader, "RecipientEmail"),
                                    PropertyDesc = S(reader, "PropertyDesc")
                                });
                            }
                        }

                        var runLogs = djRows
                            .Where(x => !string.IsNullOrWhiteSpace(x.ObjectionNo))
                            .Select(x => new NoticeRunLog
                            {
                                NoticeBatchId = batch.Id,
                                ObjectionNo = x.ObjectionNo,
                                PremiseId = x.PremiseId,
                                RecipientEmail = x.RecipientEmail,
                                PropertyDesc = x.PropertyDesc,
                                Status = RunStatus.Generated,
                                CreatedAtUtc = nowUtc
                            })
                            .ToList();

                        _db.NoticeRunLogs.AddRange(runLogs);
                        batch.NumberOfRecords = runLogs.Count;
                        await _db.SaveChangesAsync(ct);
                        return batch.Id;
                    }

                case NoticeKind.IN:
                    {
                        var isOmission = s.IsInvalidOmission ?? false;

                        var connStr = _db.Database.GetConnectionString()
                                      ?? throw new InvalidOperationException("Connection string not found.");

                        var inRows = new List<InBatchPickRow>();

                        using (var spConn = new Microsoft.Data.SqlClient.SqlConnection(connStr))
                        {
                            await spConn.OpenAsync(ct);

                            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                                "EXEC dbo.IN_Step3_InsertTop500IntoInvalidNoticeTable @RollId, @IsOmission, @BatchName, @BatchDate, @LetterDate",
                                spConn)
                            { CommandTimeout = 300 };

                            cmd.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@RollId", System.Data.SqlDbType.Int)
                            {
                                Value = s.RollId
                            });
                            cmd.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@IsOmission", System.Data.SqlDbType.Bit)
                            {
                                Value = isOmission
                            });
                            cmd.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@BatchName", System.Data.SqlDbType.NVarChar, 100)
                            {
                                Value = batchName
                            });
                            cmd.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@BatchDate", System.Data.SqlDbType.DateTime2)
                            {
                                Value = batchDateUtc
                            });
                            cmd.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@LetterDate", System.Data.SqlDbType.DateTime2)
                            {
                                Value = s.LetterDate.Date
                            });

                            using var reader = await cmd.ExecuteReaderAsync(ct);
                            while (await reader.ReadAsync(ct))
                            {
                                static string? S(Microsoft.Data.SqlClient.SqlDataReader r, string col)
                                {
                                    var ordinal = r.GetOrdinal(col);
                                    return r.IsDBNull(ordinal) ? null : r.GetValue(ordinal)?.ToString()?.Trim();
                                }

                                inRows.Add(new InBatchPickRow
                                {
                                    ObjectionNo = S(reader, "ObjectionNo"),
                                    PremiseId = S(reader, "PremiseId"),
                                    RecipientEmail = S(reader, "RecipientEmail"),
                                    PropertyDesc = S(reader, "PropertyDesc"),
                                    Kind = S(reader, "NoticeKind")
                                });
                            }
                        }

                        var runLogs = inRows
                            .Where(x => !string.IsNullOrWhiteSpace(x.ObjectionNo))
                            .Select(x => new NoticeRunLog
                            {
                                NoticeBatchId = batch.Id,
                                ObjectionNo = x.ObjectionNo,
                                PremiseId = x.PremiseId,
                                RecipientEmail = x.RecipientEmail,
                                PropertyDesc = x.PropertyDesc,
                                RecipientName = x.Kind,   // stores "Invalid Omission" / "Invalid Objection"
                                Status = RunStatus.Generated,
                                CreatedAtUtc = nowUtc
                            })
                            .ToList();

                        _db.NoticeRunLogs.AddRange(runLogs);
                        batch.NumberOfRecords = runLogs.Count;
                        await _db.SaveChangesAsync(ct);
                        return batch.Id;
                    }

                case NoticeKind.S53:
                    {
                        if (!s.AppealCloseDate.HasValue)
                            throw new InvalidOperationException("S53 batch requires AppealCloseDate to be set in Step 1.");

                        // Dates come from NoticeSettings (calculated in Step 1)
                        // so NoticeBatches.BatchDate == Objection_MVD.Batch_Date exactly
                        var s53BatchDate = s.BatchDate.HasValue ? s.BatchDate.Value.Date : batchDateUtc;
                        var s53AppealClose = s.AppealCloseDate.Value.Date;
                        var sapNo = createdBy;

                        batch.BatchDate = s53BatchDate;   // keep NoticeBatches in sync

                        // ── Dedicated SqlConnection — completely separate from EF's connection
                        // Reason: EF releases its connection after SaveChangesAsync above.
                        // Re-using it causes state conflicts when the reader is open.
                        // A fresh SqlConnection avoids all of that.
                        var connStr = _db.Database.GetConnectionString()
                                      ?? throw new InvalidOperationException("Connection string not found.");

                        var s53Rows = new List<S53BatchPickRow>();

                        using (var spConn = new Microsoft.Data.SqlClient.SqlConnection(connStr))
                        {
                            await spConn.OpenAsync(ct);

                            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                                "EXEC dbo.S53_Step3_InsertTop500IntoMvdTable " +
                                "@RollId, @BatchName, @BatchDate, @AppealCloseDate, @SapNo",
                                spConn)
                            {
                                CommandTimeout = 180
                            };

                            cmd.Parameters.AddWithValue("@RollId", s.RollId);
                            cmd.Parameters.AddWithValue("@BatchName", batchName);
                            cmd.Parameters.AddWithValue("@BatchDate", s53BatchDate);
                            cmd.Parameters.AddWithValue("@AppealCloseDate", s53AppealClose);
                            cmd.Parameters.AddWithValue("@SapNo", sapNo);

                            using var reader = await cmd.ExecuteReaderAsync(ct);

                            // The SP returns multiple result sets (one per sp_executesql call).
                            // Advance through them until we find the final SELECT with 4 columns.
                            do
                            {
                                if (reader.FieldCount == 4)
                                {
                                    while (await reader.ReadAsync(ct))
                                    {
                                        s53Rows.Add(new S53BatchPickRow
                                        {
                                            ObjectionNo = reader.IsDBNull(0) ? null : reader.GetString(0),
                                            PremiseId = reader.IsDBNull(1) ? null : reader.GetString(1),
                                            RecipientEmail = reader.IsDBNull(2) ? null : reader.GetString(2),
                                            ObjectorType = reader.IsDBNull(3) ? null : reader.GetString(3),
                                        });
                                    }
                                    break;  // found and read the data result set
                                }
                            }
                            while (await reader.NextResultAsync(ct));
                        }
                        // spConn is disposed here — fully closed before EF SaveChanges below

                        // ── Create NoticeRunLog entries ─────────────────────────────────────
                        var runLogs = s53Rows
                            .Where(x => !string.IsNullOrWhiteSpace(x.ObjectionNo))
                            .Select(x => new NoticeRunLog
                            {
                                NoticeBatchId = batch.Id,
                                ObjectionNo = x.ObjectionNo,
                                PremiseId = x.PremiseId,
                                RecipientEmail = x.RecipientEmail,
                                // RecipientName stores the ObjectorType so the print
                                // service knows which Objection_MVD row/filename to use.
                                RecipientName = x.ObjectorType,
                                Status = RunStatus.Generated,
                                CreatedAtUtc = nowUtc
                            }).ToList();

                        _db.NoticeRunLogs.AddRange(runLogs);
                        batch.NumberOfRecords = runLogs.Count;

                        await _db.SaveChangesAsync(ct);
                        return batch.Id;
                    }

                default:
                    // Remove the incomplete batch header since nothing was assigned
                    _db.NoticeBatches.Remove(batch);
                    await _db.SaveChangesAsync(ct);
                    throw new NotSupportedException($"Step3 batch creation not implemented for notice: {s.Notice}");
            }
        }

        // ── Legacy overload (kept for backward compat) ───────────────────────
        public async Task<NoticeBatch> CreateBatchAsync(int settingsId, string createdBy, CancellationToken ct)
        {
            var s = await _db.NoticeSettings.FirstOrDefaultAsync(x => x.Id == settingsId, ct)
                    ?? throw new InvalidOperationException("NoticeSettings not found.");

            if (!s.IsApproved)
                throw new InvalidOperationException("Settings not approved.");

            var roll = await _db.RollRegistry.AsNoTracking()
                           .FirstOrDefaultAsync(r => r.RollId == s.RollId, ct)
                       ?? throw new InvalidOperationException("RollRegistry not found.");

            var prefix = $"{s.Notice}_{roll.ShortCode}_";
            var last = await _db.NoticeBatches.AsNoTracking()
                .Where(x => x.NoticeSettingsId == settingsId)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            var nextNo = 1;
            if (last != null && last.BatchName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var tail = last.BatchName[prefix.Length..];
                if (int.TryParse(tail, out var parsed)) nextNo = parsed + 1;
            }

            var batchName = $"{prefix}{nextNo:0000}";
            var batchDate = DateTime.Today;

            var batch = new NoticeBatch
            {
                NoticeSettingsId = s.Id,
                BatchName = batchName,
                BatchDate = batchDate,
                CreatedBy = createdBy,
                CreatedAtUtc = DateTime.UtcNow,
                Roll = Enum.TryParse<RollCode>(roll.ShortCode, out var rc) ? rc : RollCode.GV23,
                Notice = s.Notice,
                SettingsVersionUsed = s.Version,
                BulkFromDate = s.BulkFromDate,
                BulkToDate = s.BulkToDate,
                BatchKind = $"{s.Notice} {s.Mode}",
                WorkflowKey = s.ApprovalKey ?? Guid.NewGuid(),
                RollId = s.RollId,
                Mode = s.Mode,
                Version = $"v{s.Version}",
                NumberOfRecords = 0,
                IsApproved = true,
                ApprovedBy = s.ApprovedBy,
                ApprovedAtUtc = s.ApprovedAtUtc
            };

            _db.NoticeBatches.Add(batch);
            await _db.SaveChangesAsync(ct);

            if (s.Notice == NoticeKind.S49)
            {
                var res = await _db.Set<S49AssignBatchResultDto>()
                    .FromSqlRaw("EXEC dbo.S49_Step3_AssignTop500PremisesToBatch @p0, @p1, @p2",
                        s.RollId, batchName, batchDate.Date)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ct);

                batch.NumberOfRecords = res?.PremiseCount ?? 0;
                await _db.SaveChangesAsync(ct);
            }

            return batch;
        }
    }
}