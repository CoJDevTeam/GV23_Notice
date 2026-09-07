namespace GV23_Notice.Models.DTOs.Section49Sup4
{
    public sealed class Sup4Section49SendResult
    {
        public string BatchName { get; set; } = string.Empty;

        public int Total { get; set; }

        public int Sent { get; set; }

        public int Failed { get; set; }

        public int NoEmail { get; set; }

        public bool TestMode { get; set; }

        public string? TestRecipient { get; set; }

        public string? ErrorMessage { get; set; }

        public bool Success =>
            string.IsNullOrWhiteSpace(ErrorMessage) &&
            Failed == 0;
    }
}