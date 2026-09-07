namespace GV23_Notice.Domain.Rolls
{
    public sealed class RollSection49StorageOptions
    {
        public string SignaturePath { get; set; } =
            string.Empty;

        public string PdfRootPath { get; set; } =
            string.Empty;

        public string EmailRootPath { get; set; } =
            string.Empty;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(PdfRootPath)
            ||
            !string.IsNullOrWhiteSpace(EmailRootPath)
            ||
            !string.IsNullOrWhiteSpace(SignaturePath);
    }
}