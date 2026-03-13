using GV23_Notice.Models.DTOs;
using GV23_Notice.Services.Search;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GV23_Notice.Controllers
{
    [Authorize]
    [Route("Search")]
    public sealed class NoticeSearchController : Controller
    {
        private readonly INoticeSearchService _search;

        public NoticeSearchController(INoticeSearchService search)
            => _search = search;

        [HttpGet("")]
        public IActionResult Index() => View();

        [HttpGet("GetSchemes")]
        public async Task<IActionResult> GetSchemes(string town, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(town)) return Json(new List<string>());
            return Json(await _search.GetSchemesByTownshipAsync(town, ct));
        }

        [HttpGet("GetTownships")]
        public async Task<IActionResult> GetTownships(CancellationToken ct)
            => Json(await _search.GetTownshipsAsync(ct));

        [HttpPost("Search")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Search(
            [FromForm] string mode,
            [FromForm] string? objectionNo,
            [FromForm] string? appealNo,
            [FromForm] string? township,
            [FromForm] string? scheme,
            [FromForm] string? erfNo,
            [FromForm] string? address,
            [FromForm] string? unitNo,
            CancellationToken ct)
        {
            NoticeSearchResult result = mode switch
            {
                "ObjectionNo" => await _search.SearchByObjectionNoAsync(objectionNo ?? "", ct),
                "AppealNo" => await _search.SearchByAppealNoAsync(appealNo ?? "", ct),
                _ => await _search.SearchByPropertyDescAsync(township, scheme, erfNo, address, unitNo, ct)
            };

            ViewBag.Townships = await _search.GetTownshipsAsync(ct);
            ViewBag.Mode = mode;
            return View("Index", result);
        }

        // Serve PDF inline → browser opens its built-in viewer
        [HttpGet("ViewPdf")]
        public async Task<IActionResult> ViewPdf([FromQuery] string path, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                return NotFound("PDF not found on disk.");
            var bytes = await System.IO.File.ReadAllBytesAsync(path, ct);
            return File(bytes, "application/pdf");
        }

        // Download ZIP — receives a comma-separated list of encoded file paths
        [HttpPost("Download")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Download(
            [FromForm] string filePaths,
            [FromForm] string mode,
            [FromForm] string term,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(filePaths))
                return BadRequest("No files selected.");

            var paths = filePaths
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            if (paths.Count == 0) return BadRequest("No valid paths.");

            var zipBytes = await _search.BuildZipAsync(paths, ct);

            var safeTerm = string.Join("_", (term ?? "Search").Split(Path.GetInvalidFileNameChars()));
            if (safeTerm.Length > 60) safeTerm = safeTerm[..60];
            var zipFileName = $"Notices_{safeTerm}_{DateTime.Now:yyyyMMdd_HHmm}.zip";

            await _search.LogDownloadAsync(
                User?.Identity?.Name ?? "Unknown",
                new NoticeSearchResult { SearchMode = mode ?? "Search", SearchTerm = term ?? "" },
                zipFileName, paths.Count, ct);

            return File(zipBytes, "application/zip", zipFileName);
        }
    }
}