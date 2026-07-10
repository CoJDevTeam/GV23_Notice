namespace GV23_Notice.Models.Workflow.ViewModels
{
    public sealed class ThirdPartyAppealStatsVm
    {
        public Guid WorkflowKey { get; set; }
        public int SettingsId { get; set; }
        public int RollId { get; set; }
        public int Version { get; set; }

        public string RollShortCode { get; set; } = "";
        public string RollName { get; set; } = "";
        public string ValuationPeriod { get; set; } = "";
        public string VersionText { get; set; } = "";

        public int TotalAdmins { get; set; }
        public int TotalRecords { get; set; }
        public int TotalPrinted { get; set; }
        public int TotalSent { get; set; }
        public int TotalFailed { get; set; }
        public int TotalNoEmail { get; set; }
        public int TotalMissingAdminEmail { get; set; }

        public DateTime? LastSentAt { get; set; }
        public string? LastSentBy { get; set; }

        public string ValuationEnquiriesEmail { get; set; }
            = "ValuationEnquiries@joburg.org.za";

        public List<ThirdPartyAppealAdminStatsVm> Admins { get; set; }
            = new();
    }

    public sealed class ThirdPartyAppealAdminStatsVm
    {
        public string AdminKey { get; set; } = "";
        public string AdminName { get; set; } = "";
        public string AdminEmail { get; set; } = "";
        public string ValuerEmails { get; set; } = "";
        public string DefaultCcEmails { get; set; } = "";

        public int TotalRecords { get; set; }
        public int TotalPrinted { get; set; }
        public int TotalSent { get; set; }
        public int TotalFailed { get; set; }
        public int TotalNoEmail { get; set; }

        public DateTime? LastSentAt { get; set; }
        public string? LastSentBy { get; set; }

        public bool HasAdminEmail =>
            !string.IsNullOrWhiteSpace(AdminEmail);

        public bool HasRecordsToReport =>
            TotalRecords > 0;

        public List<ThirdPartyAppealStatsDetailVm> Details { get; set; }
            = new();
    }

    public sealed class ThirdPartyAppealStatsDetailVm
    {
        public string PremiseId { get; set; } = "";
        public string AppealNo { get; set; } = "";
        public string PropertyDescription { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime? DateSent { get; set; }
        public string SentBy { get; set; } = "";
    }

    public sealed class ThirdPartyAppealAdminExcelVm
    {
        public string FileName { get; set; } = "";

        public string ContentType { get; set; } =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        public byte[] Content { get; set; } = Array.Empty<byte>();
    }

    public sealed class ThirdPartyAppealStatsSendResultVm
    {
        public bool Success { get; set; }
        public string AdminName { get; set; } = "";
        public string AdminEmail { get; set; } = "";
        public string CcEmails { get; set; } = "";
        public string ExcelPath { get; set; } = "";
        public string EmlPath { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
    }

    public sealed class ThirdPartyAppealStatsBulkSendResultVm
    {
        public int TotalAdmins { get; set; }
        public int Sent { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }

        public List<ThirdPartyAppealStatsSendResultVm> Results { get; set; }
            = new();
    }
}
