using GV23_Notice.Domain.Workflow;

namespace GV23_Notice.Models.Workflow.ViewModels
{
  
   
      public sealed class HomeIndexVm
        {
            // ── Live stats ───────────────────────────────────────────
            public int ActiveRolls { get; set; }
            public int TotalWorkflows { get; set; }
            public int Step1Confirmed { get; set; }    // IsConfirmed
            public int Step1Approved { get; set; }    // IsApproved
            public int PendingStep2 { get; set; }    // IsApproved && !IsStep2Approved
            public int Step2Approved { get; set; }    // IsStep2Approved
            public int TotalBatches { get; set; }
            public int TotalPrinted { get; set; }
            public int TotalSent { get; set; }

            // ── Recent workflows (last 8 active) ─────────────────────
            public List<RecentWorkflowRow> RecentWorkflows { get; set; } = new();
        }

        public sealed class RecentWorkflowRow
        {
            public int Id { get; set; }
            public string RollName { get; set; } = "";
            public string RollShortCode { get; set; } = "";
            public NoticeKind Notice { get; set; }
            public int Version { get; set; }
            public DateTime LetterDate { get; set; }
            public bool IsConfirmed { get; set; }
            public bool IsApproved { get; set; }
            public bool IsStep2Approved { get; set; }
            public bool HasWorkflowKey { get; set; }
            public int BatchCount { get; set; }
            public int SentCount { get; set; }
            public Guid? WorkflowKey { get; set; }
        }
    }

