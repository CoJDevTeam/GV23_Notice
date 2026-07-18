using GV23_Notice.Domain.Rolls;
using GV23_Notice.Domain.Workflow.Entities;
using GV23_Notice.Models.DTOs;
using GV23_Notice.Models.Workflow.ViewModels;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace GV23_Notice.Data
{
    public partial class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
       : base(options)
        {
        }
        public DbSet<NoticeSettings> NoticeSettings => Set<NoticeSettings>();
        public DbSet<NoticeApprovalLog> NoticeApprovalLogs => Set<NoticeApprovalLog>();
        public DbSet<CorrectionTicket> CorrectionTickets => Set<CorrectionTicket>();
        public DbSet<NoticeBatch> NoticeBatches => Set<NoticeBatch>();
        public DbSet<NoticeRunLog> NoticeRunLogs => Set<NoticeRunLog>();
        public DbSet<RollRegistry> RollRegistry => Set<RollRegistry>();

        public DbSet<NoticeTemplateApproval> NoticeTemplateApprovals => Set<NoticeTemplateApproval>();
      
        public DbSet<NoticeWorkflowAuditLog> NoticeWorkflowAuditLogs => Set<NoticeWorkflowAuditLog>();

        public DbSet<S49BatchRun> S49BatchRuns => Set<S49BatchRun>();
        public DbSet<S49BatchItem> S49BatchItems => Set<S49BatchItem>();

        public DbSet<NoticeSettingsAudit> NoticeSettingsAudits => Set<NoticeSettingsAudit>();
        public DbSet<NoticeStep2Snapshot> NoticeStep2Snapshots => Set<NoticeStep2Snapshot>();
        public DbSet<NoticeStep2CorrectionRequest> NoticeStep2CorrectionRequests => Set<NoticeStep2CorrectionRequest>();

        public DbSet<S49PendingCountDto> S49PendingCounts { get; set; }
        public DbSet<S49BatchPickRow> S49BatchPickRows => Set<S49BatchPickRow>();

        public DbSet<S51BatchPickRow> S51BatchPickRows => Set<S51BatchPickRow>();
        public DbSet<S53BatchPickRow> S53BatchPickRows => Set<S53BatchPickRow>();

        public DbSet<NoticeDownloadLog> NoticeDownloadLogs => Set<NoticeDownloadLog>();


        public DbSet<Domain.Workflow.Entities.NoticePreviewSnapshot> NoticePreviewSnapshots => Set<Domain.Workflow.Entities.NoticePreviewSnapshot>();

        public DbSet<PublicHoliday> PublicHolidays => Set<PublicHoliday>();
      
        public DbSet<S52BatchPickRow> S52BatchPickRows => Set<S52BatchPickRow>();
      
        public DbSet<DjBatchPickRow> DjBatchPickRows => Set<DjBatchPickRow>();
        public DbSet<InBatchPickRow> InBatchPickRows => Set<InBatchPickRow>();

        public DbSet<NoticeQaRun> NoticeQaRuns => Set<NoticeQaRun>();
        public DbSet<NoticeQaItem> NoticeQaItems => Set<NoticeQaItem>();

        public DbSet<NoticeCorrectionBatch> NoticeCorrectionBatches => Set<NoticeCorrectionBatch>();
        public DbSet<NoticeCorrectionItem> NoticeCorrectionItems => Set<NoticeCorrectionItem>();
        public DbSet<NoticeCorrectionEmailTemplate> NoticeCorrectionEmailTemplates => Set<NoticeCorrectionEmailTemplate>();
        public DbSet<ThirdPartyAppealApplicationNotice> ThirdPartyAppealApplicationNotices { get; set; }
        public DbSet<VabBoardHearingMember> VabBoardHearingMembers =>Set<VabBoardHearingMember>();

        public DbSet<ClaThirdPartyApplicationNotice>ClaThirdPartyApplicationNotices{ get; set; }
        public DbSet<VabBoard> VabBoards =>Set<VabBoard>();

        public DbSet<VabBoardMember> VabBoardMembers =>Set<VabBoardMember>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);


            modelBuilder.Entity<S53BatchPickRow>(entity =>
            {
                entity.HasNoKey();
                entity.ToView(null);
            });
            modelBuilder.Entity<S49BatchPickRow>().HasNoKey();
            modelBuilder.Entity<S49BatchPickRow>().ToView(null);

            modelBuilder.Entity<S51BatchPickRow>().HasNoKey();
            modelBuilder.Entity<S51BatchPickRow>().ToView(null);

            modelBuilder.Entity<S52BatchPickRow>().HasNoKey();
            modelBuilder.Entity<S52BatchPickRow>().ToView(null);

            modelBuilder.Entity<S53BatchPickRow>().HasNoKey();
            modelBuilder.Entity<S53BatchPickRow>().ToView(null);

            modelBuilder.Entity<DjBatchPickRow>().HasNoKey();
            modelBuilder.Entity<DjBatchPickRow>().ToView(null);

            modelBuilder.Entity<InBatchPickRow>().HasNoKey();
            modelBuilder.Entity<InBatchPickRow>().ToView(null);

            modelBuilder.Entity<NoticeSettings>(b =>
            {
                b.HasIndex(x => new { x.Roll, x.Notice, x.Mode, x.Version }).IsUnique();

                // Fast “latest approved” lookups
                b.HasIndex(x => new { x.Roll, x.Notice, x.Mode, x.IsApproved, x.ApprovedAtUtc });

                b.Property(x => x.Roll).HasConversion<int>();
                b.Property(x => x.Notice).HasConversion<int>();
                b.Property(x => x.Mode).HasConversion<int>();
            });

            modelBuilder.Entity<NoticeApprovalLog>(b =>
            {
                b.Property(x => x.Action).HasConversion<int>();
                b.HasIndex(x => new { x.NoticeSettingsId, x.PerformedAtUtc });

                b.HasOne(x => x.NoticeSettings)
                    .WithMany(x => x.ApprovalLogs)
                    .HasForeignKey(x => x.NoticeSettingsId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<CorrectionTicket>(b =>
            {
                b.Property(x => x.Status).HasConversion<int>();
                b.HasIndex(x => new { x.NoticeSettingsId, x.Status });

                b.HasOne(x => x.NoticeSettings)
                    .WithMany()
                    .HasForeignKey(x => x.NoticeSettingsId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<NoticeBatch>(b =>
            {
                b.Property(x => x.Roll).HasConversion<int>();
                b.Property(x => x.Notice).HasConversion<int>();

                b.HasIndex(x => x.BatchName).IsUnique();
                b.HasIndex(x => new { x.Roll, x.Notice, x.CreatedAtUtc });

                b.HasOne(x => x.NoticeSettings)
                    .WithMany(x => x.Batches)
                    .HasForeignKey(x => x.NoticeSettingsId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<NoticeRunLog>(b =>
            {
                b.Property(x => x.Status).HasConversion<int>();
                b.HasIndex(x => new { x.NoticeBatchId, x.Status });

                b.HasOne(x => x.NoticeBatch)
                    .WithMany(x => x.Runs)
                    .HasForeignKey(x => x.NoticeBatchId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<RollRegistry>(b =>
            {
                b.HasIndex(x => x.ShortCode).IsUnique();
                b.HasIndex(x => new { x.SourceDb, x.IsActive });
            });
            modelBuilder.Entity<NoticeTemplateApproval>(e =>
            {
                e.HasIndex(x => x.NoticeSettingsId);
                e.HasOne(x => x.NoticeSettings)
                 .WithMany()
                 .HasForeignKey(x => x.NoticeSettingsId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<CorrectionTicket>(e =>
            {
                e.HasIndex(x => new { x.RollId, x.Notice, x.Status });
                e.HasIndex(x => x.NoticeSettingsId);
                e.Property(x => x.RequestComment).HasMaxLength(2000);
                e.HasOne(x => x.NoticeSettings)
                 .WithMany()
                 .HasForeignKey(x => x.NoticeSettingsId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<NoticeWorkflowAuditLog>(e =>
            {
                e.HasIndex(x => new { x.NoticeSettingsId, x.Action });
                e.Property(x => x.Notes).HasMaxLength(2000);
                e.Property(x => x.MetaJson).HasMaxLength(4000);

                e.HasOne(x => x.NoticeSettings)
                 .WithMany()
                 .HasForeignKey(x => x.NoticeSettingsId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<S49BatchRun>(b =>
            {
                b.ToTable("S49BatchRuns");
                b.HasKey(x => x.Id);
                b.HasMany(x => x.Items)
                    .WithOne(i => i.BatchRun)
                    .HasForeignKey(i => i.BatchRunId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasIndex(x => new { x.SettingsId, x.BatchName }).IsUnique();
            });

            modelBuilder.Entity<S49BatchItem>(b =>
            {
                b.ToTable("S49BatchItems");
                b.HasKey(x => x.Id);
                b.HasIndex(x => new { x.BatchRunId, x.PremiseId }).IsUnique();
            });

            modelBuilder.Entity<NoticeSettingsAudit>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Step).HasMaxLength(30);
                b.Property(x => x.Action).HasMaxLength(50);
                b.Property(x => x.PerformedBy).HasMaxLength(256);

                b.HasOne(x => x.Settings)
                    .WithMany()
                    .HasForeignKey(x => x.SettingsId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<NoticePreviewSnapshot>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Variant).HasMaxLength(50);
                b.Property(x => x.Mode).HasMaxLength(20);
                b.Property(x => x.CreatedBy).HasMaxLength(256);

                b.HasOne(x => x.Settings)
                    .WithMany()
                    .HasForeignKey(x => x.SettingsId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<S49BatchPickRow>().HasNoKey();
            modelBuilder.Entity<S49BatchPickRow>().ToView(null);

            modelBuilder.Entity<S49PendingCountDto>(entity =>
            {
                entity.HasNoKey();
                entity.ToView(null); // tells EF this is NOT a table
            });

            modelBuilder.Entity<S51BatchPickRow>(entity =>
            {
                entity.HasNoKey();
                entity.ToView(null);
            });
            modelBuilder.Entity<Domain.Workflow.Entities.PublicHoliday>(b =>
            {
                b.ToTable("PublicHolidays");
                b.HasIndex(x => x.HolidayDate).IsUnique();
                b.HasIndex(x => new { x.Year, x.ValuationPeriodCode });
                b.Property(x => x.HolidayDate).HasConversion(
                    d => d.ToDateTime(TimeOnly.MinValue),
                    d => DateOnly.FromDateTime(d));
            });
            modelBuilder.Entity<NoticeQaRun>(b =>
            {
                b.ToTable("NoticeQaRuns");

                b.HasKey(x => x.Id);

                b.Property(x => x.Notice).HasConversion<int>();

                b.Property(x => x.Status)
                    .HasMaxLength(50);

                b.Property(x => x.CreatedBy)
                    .HasMaxLength(256);

                b.Property(x => x.ApprovedBy)
                    .HasMaxLength(256);

                b.Property(x => x.Comment)
                    .HasMaxLength(2000);

                b.HasIndex(x => new { x.WorkflowKey, x.Status });
                b.HasIndex(x => x.NoticeSettingsId);

                b.HasOne(x => x.NoticeSettings)
                    .WithMany()
                    .HasForeignKey(x => x.NoticeSettingsId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<NoticeQaItem>(b =>
            {
                b.ToTable("NoticeQaItems");

                b.HasKey(x => x.Id);

                b.Property(x => x.ObjectionNo).HasMaxLength(80);
                b.Property(x => x.PremiseId).HasMaxLength(80);
                b.Property(x => x.PropertyType).HasMaxLength(30);
                b.Property(x => x.PropertyDesc).HasMaxLength(512);
                b.Property(x => x.PdfPath).HasMaxLength(260);

                b.Property(x => x.NewCategoryMvd).HasMaxLength(200);
                b.Property(x => x.New2CategoryMvd).HasMaxLength(200);
                b.Property(x => x.New3CategoryMvd).HasMaxLength(200);
                b.Property(x => x.ExpectedCategory).HasMaxLength(200);

                b.Property(x => x.QaStatus).HasMaxLength(50);
                b.Property(x => x.QaComment).HasMaxLength(2000);

                b.HasIndex(x => new { x.NoticeQaRunId, x.PropertyType });
                b.HasIndex(x => x.NoticeRunLogId);

                b.HasOne(x => x.NoticeQaRun)
                    .WithMany(x => x.Items)
                    .HasForeignKey(x => x.NoticeQaRunId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.NoticeRunLog)
                    .WithMany()
                    .HasForeignKey(x => x.NoticeRunLogId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
            modelBuilder.Entity<NoticeCorrectionBatch>(entity =>
            {
                entity.ToTable("NoticeCorrectionBatches");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.CorrectionBatchName).HasMaxLength(150).IsRequired();
                entity.Property(x => x.RollShortCode).HasMaxLength(50);
                entity.Property(x => x.SourceDb).HasMaxLength(128);
                entity.Property(x => x.NoticeKind).HasMaxLength(50).IsRequired();
                entity.Property(x => x.NoticeSubKind).HasMaxLength(100);
                entity.Property(x => x.ReferenceType).HasMaxLength(50).IsRequired();
                entity.Property(x => x.ReferenceNo).HasMaxLength(100).IsRequired();
                entity.Property(x => x.CorrectionReason).HasMaxLength(500);
                entity.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
                entity.Property(x => x.PrintedBy).HasMaxLength(100);
                entity.Property(x => x.SentBy).HasMaxLength(100);
                entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
                entity.Property(x => x.SourceNoticeKind).HasMaxLength(50);
                entity.Property(x => x.PrintNoticeKind).HasMaxLength(50);
                entity.Property(x => x.PrintNoticeTitle).HasMaxLength(150);
                entity.HasMany(x => x.Items)
                    .WithOne(x => x.CorrectionBatch)
                    .HasForeignKey(x => x.CorrectionBatchId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<NoticeCorrectionItem>(entity =>
            {
                entity.ToTable("NoticeCorrectionItems");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.RollShortCode).HasMaxLength(50);
                entity.Property(x => x.SourceDb).HasMaxLength(128);
                entity.Property(x => x.NoticeKind).HasMaxLength(50).IsRequired();
                entity.Property(x => x.NoticeSubKind).HasMaxLength(100);
                entity.Property(x => x.ReferenceType).HasMaxLength(50).IsRequired();
                entity.Property(x => x.ReferenceNo).HasMaxLength(100).IsRequired();

                entity.Property(x => x.ObjectionNo).HasMaxLength(100);
                entity.Property(x => x.AppealNo).HasMaxLength(100);
                entity.Property(x => x.QueryNo).HasMaxLength(100);
                entity.Property(x => x.ReviewNo).HasMaxLength(100);

                entity.Property(x => x.PremiseId).HasMaxLength(100);
                entity.Property(x => x.UnitKey).HasMaxLength(100);
                entity.Property(x => x.ValuationKey).HasMaxLength(100);

                entity.Property(x => x.PropertyType).HasMaxLength(100);
                entity.Property(x => x.RecipientRole).HasMaxLength(100);
                entity.Property(x => x.RecipientName).HasMaxLength(255);
                entity.Property(x => x.RecipientEmail).HasMaxLength(500);

                entity.Property(x => x.ADDR1).HasMaxLength(255);
                entity.Property(x => x.ADDR2).HasMaxLength(255);
                entity.Property(x => x.ADDR3).HasMaxLength(255);
                entity.Property(x => x.ADDR4).HasMaxLength(255);
                entity.Property(x => x.ADDR5).HasMaxLength(255);

                entity.Property(x => x.OldCategory).HasMaxLength(255);
                entity.Property(x => x.OldCategory2).HasMaxLength(255);
                entity.Property(x => x.OldCategory3).HasMaxLength(255);

                entity.Property(x => x.OldMarketValue).HasMaxLength(100);
                entity.Property(x => x.OldMarketValue2).HasMaxLength(100);
                entity.Property(x => x.OldMarketValue3).HasMaxLength(100);

                entity.Property(x => x.OldExtent).HasMaxLength(100);
                entity.Property(x => x.OldExtent2).HasMaxLength(100);
                entity.Property(x => x.OldExtent3).HasMaxLength(100);

                entity.Property(x => x.NewCategory).HasMaxLength(255);
                entity.Property(x => x.NewCategory2).HasMaxLength(255);
                entity.Property(x => x.NewCategory3).HasMaxLength(255);

                entity.Property(x => x.NewMarketValue).HasMaxLength(100);
                entity.Property(x => x.NewMarketValue2).HasMaxLength(100);
                entity.Property(x => x.NewMarketValue3).HasMaxLength(100);

                entity.Property(x => x.NewExtent).HasMaxLength(100);
                entity.Property(x => x.NewExtent2).HasMaxLength(100);
                entity.Property(x => x.NewExtent3).HasMaxLength(100);

                entity.Property(x => x.WEFDate).HasMaxLength(100);
                entity.Property(x => x.WEFDate2).HasMaxLength(100);
                entity.Property(x => x.WEFDate3).HasMaxLength(100);

                entity.Property(x => x.Section51Pin).HasMaxLength(100);
                entity.Property(x => x.Section52Review).HasMaxLength(50);

                entity.Property(x => x.EmailSubject).HasMaxLength(500);
                entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
                entity.Property(x => x.EmailCc).HasMaxLength(1000);
                entity.Property(x => x.SourceNoticeKind).HasMaxLength(50);
                entity.Property(x => x.PrintNoticeKind).HasMaxLength(50);
                entity.Property(x => x.PrintNoticeTitle).HasMaxLength(150);
            });

            modelBuilder.Entity<NoticeCorrectionEmailTemplate>(entity =>
            {
                entity.ToTable("NoticeCorrectionEmailTemplates");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.NoticeKind).HasMaxLength(50).IsRequired();
                entity.Property(x => x.NoticeSubKind).HasMaxLength(100);
                entity.Property(x => x.TemplateName).HasMaxLength(150).IsRequired();
                entity.Property(x => x.SubjectTemplate).HasMaxLength(500).IsRequired();
                entity.Property(x => x.CreatedBy).HasMaxLength(100);
                entity.Property(x => x.CcTemplate).HasMaxLength(1000);
            });
            modelBuilder.Entity<ThirdPartyAppealApplicationNotice>(entity =>
            {
                entity.ToTable("ThirdPartyAppealApplicationNotices", "dbo");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Appeal_No)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(x => x.Objection_No).HasMaxLength(100);
                entity.Property(x => x.Premise_ID).HasMaxLength(100);
                entity.Property(x => x.Valuation_Key).HasMaxLength(100);
                entity.Property(x => x.Unit_Key).HasMaxLength(100);
                entity.Property(x => x.Property_ID).HasMaxLength(100);

                entity.Property(x => x.RollShortCode).HasMaxLength(50);
                entity.Property(x => x.ValuationPeriod).HasMaxLength(250);

                entity.Property(x => x.Appeal_Type).HasMaxLength(100);
                entity.Property(x => x.Appeal_Status).HasMaxLength(100);
                entity.Property(x => x.Property_Type).HasMaxLength(100);
                entity.Property(x => x.Township).HasMaxLength(250);

                entity.Property(x => x.OwnerName).HasMaxLength(300);
                entity.Property(x => x.OwnerEmail).HasMaxLength(300);
                entity.Property(x => x.OwnerCell).HasMaxLength(100);

                entity.Property(x => x.ThirdPartyName).HasMaxLength(300);
                entity.Property(x => x.ThirdPartyEmail).HasMaxLength(300);
                entity.Property(x => x.ThirdPartyCell).HasMaxLength(100);

                entity.Property(x => x.AdminName).HasMaxLength(300);
                entity.Property(x => x.AdminEmail).HasMaxLength(300);

                entity.Property(x => x.EmailTo).HasMaxLength(300);
                entity.Property(x => x.EmailCc).HasMaxLength(1000);

                entity.Property(x => x.Status)
                    .HasMaxLength(80)
                    .HasDefaultValue("Pending");

                entity.Property(x => x.CreatedAt)
                    .HasDefaultValueSql("GETDATE()");

                entity.HasIndex(x => x.Appeal_No)
                    .HasDatabaseName("IX_ThirdPartyAppealApplicationNotices_AppealNo");

                entity.HasIndex(x => x.Status)
                    .HasDatabaseName("IX_ThirdPartyAppealApplicationNotices_Status");

                entity.HasIndex(x => new { x.RollId, x.Status })
                    .HasDatabaseName("IX_ThirdPartyAppealApplicationNotices_Roll_Status");
            });
            modelBuilder.Entity<VabBoard>(entity =>
            {
                entity.ToTable("VabBoards");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.BoardCode)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(x => x.BoardName)
                    .HasMaxLength(250)
                    .IsRequired();

                entity.HasIndex(x => x.BoardCode)
                    .IsUnique();

                entity.HasMany(x => x.Members)
                    .WithOne(x => x.VabBoard)
                    .HasForeignKey(x => x.VabBoardId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<VabBoardMember>(entity =>
            {
                entity.ToTable("VabBoardMembers");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.MemberRole)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(x => x.NameAndSurname)
                    .HasMaxLength(250)
                    .IsRequired();

                entity.Property(x => x.CojValuerTeam)
                    .HasMaxLength(200);

                entity.Property(x => x.CojEmail)
                    .HasMaxLength(320);

                entity.Property(x => x.EmailAddress)
                    .HasMaxLength(320);
            });

            modelBuilder.Entity<ThirdPartyAppealApplicationNotice>()
                .HasOne(x => x.VabBoard)
                .WithMany()
                .HasForeignKey(x => x.VabBoardId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
