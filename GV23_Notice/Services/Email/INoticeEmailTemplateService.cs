namespace GV23_Notice.Services.Email
{
    public interface INoticeEmailTemplateService
    {
        (string Subject, string BodyHtml) Build(NoticeEmailRequest req);
    }
}
