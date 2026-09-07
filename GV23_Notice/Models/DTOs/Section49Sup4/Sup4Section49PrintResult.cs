namespace GV23_Notice.Models.DTOs.Section49Sup4
{
    public sealed class Sup4Section49PrintResult
    {
        public string BatchName { get; set; } = string.Empty;

        public int Total { get; set; }

        public int Printed { get; set; }

        public int Failed { get; set; }

        public List<string> FailedPremiseIds { get; set; } = new();

        public bool IsComplete =>
            Total == 500 &&
            Printed == 500 &&
            Failed == 0;
    }
}