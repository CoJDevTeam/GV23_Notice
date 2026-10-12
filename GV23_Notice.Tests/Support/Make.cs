using GV23_Notice.Domain.Rolls;
using GV23_Notice.Domain.Workflow;
using GV23_Notice.Domain.Workflow.Entities;

namespace GV23_Notice.Tests.Support
{
    /// <summary>Small builders for test data with sensible defaults.</summary>
    public static class Make
    {
        public const int Supp4RollId = 5;

        public static RollRegistry Supp4Roll() => new()
        {
            RollId = Supp4RollId,
            ShortCode = "SUPP 4",
            Name = "Supplementary Valuation Roll 4",
            RollType = "Supp",
            SourceDb = "Objection_Supp4",
            IsActive = true,
            CreatedBy = "test"
        };

        public static RollRegistry Gv23Roll() => new()
        {
            RollId = 1,
            ShortCode = "GV23",
            Name = "General Valuation Roll 2023",
            RollType = "GV",
            SourceDb = "Objection",
            IsActive = true,
            CreatedBy = "test"
        };

        public static NoticeSettings Settings(
            int id = 1,
            NoticeKind notice = NoticeKind.S49,
            int rollId = Supp4RollId,
            RollCode roll = RollCode.SUPP4,
            int version = 1,
            bool confirmed = true,
            bool approved = true,
            Guid? approvalKey = null) => new()
        {
            Id = id,
            Notice = notice,
            RollId = rollId,
            Roll = roll,
            Mode = BatchMode.Bulk,
            Version = version,
            LetterDate = new DateTime(2026, 10, 9),
            ObjectionStartDate = new DateTime(2026, 10, 9),
            ObjectionEndDate = new DateTime(2026, 11, 9),
            IsConfirmed = confirmed,
            IsApproved = approved,
            ApprovalKey = approvalKey,
            SignaturePath = notice == NoticeKind.S49 ? @"C:\sig.png" : null
        };

        public static NoticeBatch Batch(
            int id,
            int settingsId,
            Guid workflowKey,
            string name,
            int records,
            NoticeKind notice = NoticeKind.S49,
            int rollId = Supp4RollId,
            string batchKind = "STEP3") => new()
        {
            Id = id,
            NoticeSettingsId = settingsId,
            WorkflowKey = workflowKey,
            BatchName = name,
            NumberOfRecords = records,
            Notice = notice,
            RollId = rollId,
            BatchKind = batchKind,
            CreatedBy = "test"
        };

        public static NoticeRunLog PrintedLog(int id, int batchId, string premiseId) => new()
        {
            Id = id,
            NoticeBatchId = batchId,
            PremiseId = premiseId,
            Status = RunStatus.Printed,
            PdfPath = $@"C:\pdf\{premiseId}.pdf",
            RecipientEmail = $"{premiseId}@example.com"
        };

        public static NoticeQaRun ApprovedQaRun(int id, int settingsId, Guid workflowKey, params int[] runLogIds)
        {
            var run = new NoticeQaRun
            {
                Id = id,
                NoticeSettingsId = settingsId,
                WorkflowKey = workflowKey,
                RollId = Supp4RollId,
                Notice = NoticeKind.S49,
                Status = "Approved",
                CreatedBy = "test",
                ApprovedBy = "test"
            };

            foreach (var logId in runLogIds)
            {
                run.Items.Add(new NoticeQaItem
                {
                    NoticeRunLogId = logId,
                    IsCategoryValid = true,
                    QaStatus = "Passed"
                });
            }

            return run;
        }
    }
}
