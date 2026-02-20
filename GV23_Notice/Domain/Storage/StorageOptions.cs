namespace GV23_Notice.Domain.Storage
{
    public sealed class StorageOptions
    {
        public string SignatureFolderName { get; set; } = "Signature Folder";
        public string NoticeFolderNameSuffix { get; set; } = " Outcome";

        public Dictionary<string, string> ObjectionRootsByShortCode { get; set; } = new();
        public Dictionary<string, string> AppealRootsByShortCode { get; set; } = new();
    }
}
