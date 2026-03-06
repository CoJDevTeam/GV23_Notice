namespace GV23_Notice.Services.Email
{
        public interface INoticeBatchEmailService
        {
            /// <summary>
            /// Send emails for the selected batch IDs.
            /// Total records across all selected batches must not exceed MaxSendPerBatch (2000).
            /// </summary>
            Task<SendBatchEmailResult> SendBatchEmailsAsync(
                IEnumerable<int> batchIds,
                Guid workflowKey,
                string sentBy,
                CancellationToken ct);

            /// <summary>Preview total record count for selected batches (for UI validation).</summary>
            Task<int> CountSelectedRecordsAsync(
                IEnumerable<int> batchIds,
                CancellationToken ct);
        }

        public sealed class SendBatchEmailResult
        {
            public int BatchesProcessed { get; set; }
            public int Sent { get; set; }
            public int Failed { get; set; }
            public int Skipped { get; set; }   // already sent or no email
            public string? ErrorMessage { get; set; }
        }
    }


