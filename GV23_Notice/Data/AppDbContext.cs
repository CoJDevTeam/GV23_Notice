using GV23_Notice.Domain.Rolls;
using GV23_Notice.Domain.Workflow.Entities;
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
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

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
        }
    }
}
