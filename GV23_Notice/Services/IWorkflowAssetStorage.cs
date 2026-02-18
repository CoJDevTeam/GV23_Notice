using GV23_Notice.Domain.Workflow;

namespace GV23_Notice.Services
{
    public interface IWorkflowAssetStorage
    {
        Task<string> SaveSignatureAsync(int rollId, NoticeKind notice, int settingsVersion, IFormFile file, CancellationToken ct);
        Task<string> SaveOverrideEvidenceAsync(int rollId, NoticeKind notice, int settingsVersion, IFormFile file, CancellationToken ct);
    }

    public sealed class WorkflowAssetStorage : IWorkflowAssetStorage
    {
        private readonly IStorageRootResolver _roots;

        public WorkflowAssetStorage(IStorageRootResolver roots)
        {
            _roots = roots;
        }

        private static string NoticeShort(NoticeKind n) => n switch
        {
            NoticeKind.S49 => "S49",
            NoticeKind.S51 => "S51",
            NoticeKind.S52 => "S52",
            NoticeKind.S53 => "S53",
            NoticeKind.DJ => "DJ",
            NoticeKind.IN => "IN",
            NoticeKind.S78 => "S78",
            _ => n.ToString()
        };

        public Task<string> SaveSignatureAsync(int rollId, NoticeKind notice, int settingsVersion, IFormFile file, CancellationToken ct)
            => SaveAssetAsync(rollId, notice, settingsVersion, "signature", file, ct);

        public Task<string> SaveOverrideEvidenceAsync(int rollId, NoticeKind notice, int settingsVersion, IFormFile file, CancellationToken ct)
            => SaveAssetAsync(rollId, notice, settingsVersion, "override-evidence", file, ct);

        private async Task<string> SaveAssetAsync(int rollId, NoticeKind notice, int version, string prefix, IFormFile file, CancellationToken ct)
        {
            if (file == null || file.Length <= 0)
                throw new InvalidOperationException("File is required.");

            var roll = await _roots.GetRollAsync(rollId, ct);
            var sigFolder = await _roots.GetSignatureFolderAsync(rollId, DataDomain.Objection, ct);

            var dir = Path.Combine(sigFolder, $"{roll.ShortCode}_{NoticeShort(notice)}", $"v{version}");
            Directory.CreateDirectory(dir);

            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".bin";

            var path = Path.Combine(dir, $"{prefix}{ext}");

            await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await file.CopyToAsync(fs, ct);

            return path;
        }
    }
}
