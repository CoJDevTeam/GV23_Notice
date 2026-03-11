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


        public DbSet<Domain.Workflow.Entities.NoticePreviewSnapshot> NoticePreviewSnapshots => Set<Domain.Workflow.Entities.NoticePreviewSnapshot>();

        public DbSet<PublicHoliday> PublicHolidays => Set<PublicHoliday>();
      
        public DbSet<S52BatchPickRow> S52BatchPickRows => Set<S52BatchPickRow>();
      
        public DbSet<DjBatchPickRow> DjBatchPickRows => Set<DjBatchPickRow>();
        public DbSet<InBatchPickRow> InBatchPickRows => Set<InBatchPickRow>();


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
        }
    }
}
