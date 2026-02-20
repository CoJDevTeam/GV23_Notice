namespace GV23_Notice.Services.Step3
{
    public interface IWorkflowEmailReadService
    {
        (string Subject, string BodyHtml) TryReadEml(string? emlPath);
    }
}
