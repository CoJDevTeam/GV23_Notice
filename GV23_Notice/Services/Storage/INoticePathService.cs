using GV23_Notice.Domain.Rolls;
using GV23_Notice.Domain.Workflow;

namespace GV23_Notice.Services.Storage
{
    public interface INoticePathService
    {
        string GetRootPath(RollRegistry roll, NoticeKind notice);
        string BuildPdfPath(
        RollRegistry roll,
        NoticeKind notice,
        string keyNo,
        string propertyDesc,
        string? copyRole);
    }
}
