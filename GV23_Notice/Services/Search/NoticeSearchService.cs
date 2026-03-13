using GV23_Notice.Data;
using GV23_Notice.Domain.Storage;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models.DTOs;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Data;
using System.IO.Compression;

namespace GV23_Notice.Services.Search
{
   
    public sealed class NoticeSearchService : INoticeSearchService
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly StorageOptions _storage;

        public NoticeSearchService(
            AppDbContext db,
            IConfiguration config,
            IOptions<StorageOptions> storage)
        {
            _db = db;
            _config = config;
            _storage = storage.Value;
        }

        // ── Townships ────────────────────────────────────────────────────────
        public async Task<List<string>> GetTownshipsAsync(CancellationToken ct)
        {
            var list = new List<string>();
            await using var conn = new SqlConnection(DefaultCs());
            await using var cmd = new SqlCommand("Notice_DB.dbo.Notice_GetAllTownships", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };
            await conn.OpenAsync(ct);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var v = r.IsDBNull(0) ? null : r.GetString(0);
                if (!string.IsNullOrWhiteSpace(v)) list.Add(v.Trim());
            }
            return list;
        }

        // ── Schemes for a township ───────────────────────────────────────────
        public async Task<List<string>> GetSchemesByTownshipAsync(string town, CancellationToken ct)
        {
            var list = new List<string>();
            await using var conn = new SqlConnection(DefaultCs());
            await using var cmd = new SqlCommand("Notice_DB.dbo.Notice_GetSchemesByTownship", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };
            cmd.Parameters.AddWithValue("@TownNameDesc", town);
            await conn.OpenAsync(ct);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var v = r.IsDBNull(0) ? null : r.GetString(0);
                if (!string.IsNullOrWhiteSpace(v)) list.Add(v.Trim());
            }
            return list;
        }

        // ── Search by Objection No ───────────────────────────────────────────
        public async Task<NoticeSearchResult> SearchByObjectionNoAsync(string term, CancellationToken ct)
        {
            var rows = await RunSearchSpAsync("ObjectionNo", term.Trim(), ct);
            var files = await ResolveFilesAsync(rows, ct);
            return new NoticeSearchResult { SearchMode = "ObjectionNo", SearchTerm = term.Trim(), Files = files };
        }

        // ── Search by Appeal No ──────────────────────────────────────────────
        public async Task<NoticeSearchResult> SearchByAppealNoAsync(string term, CancellationToken ct)
        {
            var rows = await RunSearchSpAsync("AppealNo", term.Trim(), ct);
            var files = await ResolveFilesAsync(rows, ct);
            return new NoticeSearchResult { SearchMode = "AppealNo", SearchTerm = term.Trim(), Files = files };
        }

        // ── Search by Property Description ───────────────────────────────────
        // Strategy:
        //   If user supplies Township → S49 roll table search (exact township, optional ERF)
        //   +  LIKE fallback on Obj_Property_Info / Appeal for all notice types
        public async Task<NoticeSearchResult> SearchByPropertyDescAsync(
            string? township, string? scheme, string? erfNo,
            string? address, string? unitNo, CancellationToken ct)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(township)) parts.Add($"Township:{township!.Trim()}");
            if (!string.IsNullOrWhiteSpace(scheme)) parts.Add($"Scheme:{scheme!.Trim()}");
            if (!string.IsNullOrWhiteSpace(erfNo)) parts.Add($"ERF:{erfNo!.Trim()}");
            if (!string.IsNullOrWhiteSpace(address)) parts.Add($"Addr:{address!.Trim()}");
            if (!string.IsNullOrWhiteSpace(unitNo)) parts.Add($"Unit:{unitNo!.Trim()}");
            var label = string.Join(" | ", parts);

            var rows = new List<PropertyRow>();

            if (!string.IsNullOrWhiteSpace(township))
            {
                // Check roll tables (S49) using exact township + optional ERF
                var rollRows = await RunS49SpAsync(township!.Trim(), erfNo?.Trim(), ct);
                rows.AddRange(rollRows);

                // Also search objection/appeal tables with the most specific term
                var likeTerm = !string.IsNullOrWhiteSpace(erfNo) ? erfNo!.Trim()
                             : !string.IsNullOrWhiteSpace(scheme) ? scheme!.Trim()
                             : !string.IsNullOrWhiteSpace(address) ? address!.Trim()
                             : township.Trim();
                var objRows = await RunSearchSpAsync("PropertyDesc", likeTerm, ct);
                rows.AddRange(objRows);
            }
            else
            {
                var term = !string.IsNullOrWhiteSpace(address) ? address!.Trim()
                         : !string.IsNullOrWhiteSpace(unitNo) ? unitNo!.Trim()
                         : "";
                if (!string.IsNullOrWhiteSpace(term))
                    rows = await RunSearchSpAsync("PropertyDesc", term, ct);
            }

            var files = await ResolveFilesAsync(rows, ct);
            return new NoticeSearchResult { SearchMode = "PropertyDesc", SearchTerm = label, Files = files };
        }

        // ── Build ZIP from a list of absolute file paths ─────────────────────
        public async Task<byte[]> BuildZipAsync(IEnumerable<string> filePaths, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            using var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in filePaths.Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p)))
            {
                var name = Path.GetFileName(path);
                if (used.Contains(name))
                {
                    var ext = Path.GetExtension(path);
                    var stem = Path.GetFileNameWithoutExtension(path);
                    var i = 1;
                    while (used.Contains(name)) name = $"{stem}_{i++}{ext}";
                }
                used.Add(name);
                var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
                await using var s = entry.Open();
                var bytes = await File.ReadAllBytesAsync(path, ct);
                await s.WriteAsync(bytes, ct);
            }

            zip.Dispose();
            return ms.ToArray();
        }

        // ── Get PDF path for single RunLog (View PDF action) ─────────────────
        public async Task<string?> GetPdfPathAsync(int runLogId, CancellationToken ct)
        {
            var rl = await _db.NoticeRunLogs.AsNoTracking()
                         .FirstOrDefaultAsync(r => r.Id == runLogId, ct);
            return rl?.PdfPath;
        }

        // ── Log download ─────────────────────────────────────────────────────
        public async Task LogDownloadAsync(
            string downloadedBy, NoticeSearchResult result,
            string zipFileName, int fileCount, CancellationToken ct)
        {
            var first = result.Files.FirstOrDefault();
            _db.NoticeDownloadLogs.Add(new NoticeDownloadLog
            {
                DownloadedBy = downloadedBy,
                DownloadedAtUtc = DateTime.UtcNow,
                SearchMode = result.SearchMode,
                SearchTerm = result.SearchTerm,
                ObjectionNo = first?.ObjectionNo,
                AppealNo = first?.AppealNo,
                PropertyDesc = first?.PropertyDesc,
                FileCount = fileCount,
                ZipFileName = zipFileName
            });
            await _db.SaveChangesAsync(ct);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  PRIVATE — SP execution
        // ═══════════════════════════════════════════════════════════════════

        private sealed record PropertyRow(
            int RollId,
            string ShortCode,
            string SourceDb,
            string? ObjectionNo,
            string? AppealNo,
            string? PropertyDesc,
            string? ObjectionStatus,
            string TableSource);

        // ── S49: search roll tables with Township + optional ERF ─────────────
        private async Task<List<PropertyRow>> RunS49SpAsync(
            string township, string? erfNo, CancellationToken ct)
        {
            var rows = new List<PropertyRow>();
            await using var conn = new SqlConnection(DefaultCs());
            await using var cmd = new SqlCommand("dbo.Notice_SearchProperties", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 120
            };
            cmd.Parameters.AddWithValue("@SearchMode", "S49");
            cmd.Parameters.AddWithValue("@SearchTerm", "");
            cmd.Parameters.AddWithValue("@Township", township);
            cmd.Parameters.Add("@ErfNo", SqlDbType.NVarChar, 50).Value =
                string.IsNullOrWhiteSpace(erfNo) ? (object)DBNull.Value : erfNo;

            await conn.OpenAsync(ct);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            rows = await ReadPropertyRows(reader, ct);
            return rows;
        }

        // ── Shared reader helper ──────────────────────────────────────────────
        private static async Task<List<PropertyRow>> ReadPropertyRows(
            SqlDataReader reader, CancellationToken ct)
        {
            var rows = new List<PropertyRow>();
            string? Str(int i) => reader.IsDBNull(i) ? null : reader.GetString(i).Trim();
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new PropertyRow(
                    RollId: reader.GetInt32(0),
                    ShortCode: reader.GetString(1).Trim(),
                    SourceDb: reader.GetString(2).Trim(),
                    ObjectionNo: Str(3),
                    AppealNo: Str(4),
                    PropertyDesc: Str(5),
                    ObjectionStatus: Str(6),
                    TableSource: reader.GetString(7).Trim()
                ));
            }
            return rows;
        }

        private async Task<List<PropertyRow>> RunSearchSpAsync(
            string mode, string term, CancellationToken ct)
        {
            var rows = new List<PropertyRow>();
            if (string.IsNullOrWhiteSpace(term)) return rows;

            await using var conn = new SqlConnection(DefaultCs());
            await using var cmd = new SqlCommand("dbo.Notice_SearchProperties", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 120
            };
            cmd.Parameters.AddWithValue("@SearchMode", mode);
            cmd.Parameters.AddWithValue("@SearchTerm", term);

            await conn.OpenAsync(ct);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await ReadPropertyRows(reader, ct);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  PRIVATE — Resolve files from disk for each property row
        // ═══════════════════════════════════════════════════════════════════

        private Task<List<NoticeFileResult>> ResolveFilesAsync(
            List<PropertyRow> rows, CancellationToken ct)
        {
            var results = new List<NoticeFileResult>();

            foreach (var row in rows)
            {
                // ── Objection root: scan {root}\{ObjectionNo}\ ────────────────
                if (!string.IsNullOrWhiteSpace(row.ObjectionNo)
                    && _storage.ObjectionRootsByShortCode.TryGetValue(row.ShortCode, out var objRoot)
                    && !string.IsNullOrWhiteSpace(objRoot))
                {
                    var folder = Path.Combine(objRoot, SanitiseName(row.ObjectionNo));
                    ScanFolder(folder, row, results, isAppeal: false);
                }

                // ── Appeal root: scan {root}\{AppealNo}\ ─────────────────────
                if (!string.IsNullOrWhiteSpace(row.AppealNo)
                    && _storage.AppealRootsByShortCode.TryGetValue(row.ShortCode, out var appRoot)
                    && !string.IsNullOrWhiteSpace(appRoot))
                {
                    var folder = Path.Combine(appRoot, SanitiseName(row.AppealNo));
                    ScanFolder(folder, row, results, isAppeal: true);
                }

                // If no files found at all but we have a matching property row,
                // still show the property (with PdfExists=false, EmlExists=false)
                // so the admin can see the objection_Status
                if (!results.Any(r =>
                        r.ObjectionNo == (row.ObjectionNo ?? "") &&
                        r.AppealNo == row.AppealNo &&
                        r.RollName == row.ShortCode))
                {
                    results.Add(new NoticeFileResult
                    {
                        ObjectionNo = row.ObjectionNo ?? "",
                        AppealNo = row.AppealNo,
                        PropertyDesc = row.PropertyDesc ?? "",
                        ObjectionStatus = row.ObjectionStatus ?? "",
                        RollName = row.ShortCode,
                        TableSource = row.TableSource,
                        NoticeType = "—",
                        PdfExists = false,
                        EmlExists = false
                    });
                }
            }

            return Task.FromResult(results);
        }

        /// <summary>
        /// Scans a property folder recursively, creating one NoticeFileResult
        /// per PDF found (pairing its matching .eml file if present).
        /// </summary>
        private static void ScanFolder(
            string folder,
            PropertyRow row,
            List<NoticeFileResult> results,
            bool isAppeal)
        {
            if (!Directory.Exists(folder)) return;

            // Find every PDF inside any sub-folder
            var pdfs = Directory.GetFiles(folder, "*.pdf", SearchOption.AllDirectories);

            foreach (var pdf in pdfs)
            {
                // Derive matching .eml path (same directory, same stem)
                var emlPath = Path.ChangeExtension(pdf, ".eml");

                // Derive notice type from the sub-folder name
                var subDir = Path.GetDirectoryName(pdf) ?? folder;
                var subDirName = Path.GetFileName(subDir);
                var noticeType = DeriveNoticeType(subDirName);

                // Derive objector type from filename suffix
                var fileName = Path.GetFileNameWithoutExtension(pdf);
                var objType = DeriveObjType(fileName);

                results.Add(new NoticeFileResult
                {
                    ObjectionNo = row.ObjectionNo ?? "",
                    AppealNo = row.AppealNo,
                    PropertyDesc = row.PropertyDesc ?? "",
                    ObjectionStatus = row.ObjectionStatus ?? "",
                    RollName = row.ShortCode,
                    TableSource = isAppeal ? "Appeal" : "Objection",
                    NoticeType = noticeType,
                    ObjectorType = objType,
                    PdfPath = pdf,
                    EmlPath = File.Exists(emlPath) ? emlPath : null,
                    PdfExists = true,
                    EmlExists = File.Exists(emlPath)
                });
            }

            // Also find any .eml files that don't have a matching PDF
            var emls = Directory.GetFiles(folder, "*.eml", SearchOption.AllDirectories);
            foreach (var eml in emls)
            {
                var matchingPdf = Path.ChangeExtension(eml, ".pdf");
                if (File.Exists(matchingPdf)) continue; // already covered above

                var subDir = Path.GetDirectoryName(eml) ?? folder;
                var subDirName = Path.GetFileName(subDir);
                var noticeType = DeriveNoticeType(subDirName);
                var fileName = Path.GetFileNameWithoutExtension(eml);
                var objType = DeriveObjType(fileName);

                results.Add(new NoticeFileResult
                {
                    ObjectionNo = row.ObjectionNo ?? "",
                    AppealNo = row.AppealNo,
                    PropertyDesc = row.PropertyDesc ?? "",
                    ObjectionStatus = row.ObjectionStatus ?? "",
                    RollName = row.ShortCode,
                    TableSource = isAppeal ? "Appeal" : "Objection",
                    NoticeType = noticeType,
                    ObjectorType = objType,
                    PdfPath = null,
                    EmlPath = eml,
                    PdfExists = false,
                    EmlExists = true
                });
            }
        }

        /// <summary>
        /// Maps a sub-folder name to a human-readable notice type.
        /// Folder names come from NoticePathService (e.g. "Section 51 Notice",
        /// "Section 53 MVD", "DearJohnny_Notice", etc.)
        /// </summary>
        private static string DeriveNoticeType(string folderName)
        {
            var f = folderName.ToUpperInvariant();
            if (f.Contains("49")) return "S49";
            if (f.Contains("51")) return "S51";
            if (f.Contains("52")) return "S52";
            if (f.Contains("53")) return "S53";
            if (f.Contains("78")) return "S78";
            if (f.Contains("DEAR") || f.Contains("DJ")) return "DJ";
            if (f.Contains("INVALID")) return "IN";
            if (f.Contains("APPEAL")) return "Appeal";
            return folderName;
        }

        /// <summary>
        /// Tries to derive the objector type from the filename suffix,
        /// e.g. "..._Owner_Rep_MVD" → "Owner_Rep"
        /// </summary>
        private static string DeriveObjType(string stem)
        {
            if (stem.Contains("Owner_Rep", StringComparison.OrdinalIgnoreCase)) return "Owner_Rep";
            if (stem.Contains("Owner_Third_Party", StringComparison.OrdinalIgnoreCase)) return "Owner_Third_Party";
            if (stem.Contains("Representative", StringComparison.OrdinalIgnoreCase)) return "Representative";
            if (stem.Contains("Third_Party", StringComparison.OrdinalIgnoreCase)) return "Third_Party";
            if (stem.Contains("Owner", StringComparison.OrdinalIgnoreCase)) return "Owner";
            return "";
        }

        private static string SanitiseName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Concat(name.Trim().Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '_' : c))
                         .Trim('_');
        }

        private string DefaultCs() =>
            _config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection not configured.");
    }
}