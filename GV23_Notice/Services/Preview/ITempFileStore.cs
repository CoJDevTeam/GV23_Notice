namespace GV23_Notice.Services.Preview
{
    public interface ITempFileStore
    {
        Task<string> SavePdfAsync(byte[] pdfBytes, string fileName, CancellationToken ct);
    }
}
