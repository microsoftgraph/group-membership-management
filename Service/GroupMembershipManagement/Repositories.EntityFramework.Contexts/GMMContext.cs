// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;

namespace Repositories.EntityFramework.Contexts
{
    public class GMMContext : DbContext
    {
        public DbSet<SyncJob> SyncJobs { get; set; } = null!;
        public DbSet<PurgedSyncJob> PurgedSyncJobs { get; set; } = null!;
        public DbSet<Status> Statuses { get; set; } = null!;
        public DbSet<Setting> Settings { get; set; } = null!;

        public DbSet<EmailType> EmailTypes { get; set; }
        public DbSet<JobEmailStatus> JobEmailStatuses { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SyncJob>().Property(t => t.Id)
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("NEWSEQUENTIALID()");

            modelBuilder.Entity<PurgedSyncJob>().Property(p => p.Id)
                  .ValueGeneratedOnAdd()
                  .HasDefaultValueSql("NEWID()");

            modelBuilder.Entity<Setting>().HasKey(s => s.Id);
            modelBuilder.Entity<Setting>().Property(s => s.Id)
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("NEWSEQUENTIALID()");

            modelBuilder.Entity<Setting>().HasIndex(s => s.Key).IsUnique();

            modelBuilder.Entity<SyncJob>()
                        .HasOne(s => s.StatusDetails)
                        .WithOne()
                        .HasForeignKey<SyncJob>(x => x.Status)
                        .HasPrincipalKey<Status>(x => x.Name)
                        .IsRequired(false)
                        .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Status>()
                        .ToTable("Statuses");
            modelBuilder.Entity<Setting>().HasKey(s => s.Key);
            modelBuilder.Entity<JobEmailStatus>().Property(t => t.JobEmailStatusId)
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("NEWSEQUENTIALID()");

            modelBuilder.Entity<EmailType>().HasData(
                    new EmailType { EmailTypeId = 1, EmailTypeName = "OnBoarding", EmailContentTemplateName = "SyncStartedEmailBody" },
                    new EmailType { EmailTypeId = 2, EmailTypeName = "OnBoarding", EmailContentTemplateName = "SyncCompletedEmailBody" }
                );

            modelBuilder.Entity<JobEmailStatus>()
                .HasOne(j => j.SyncJob)
                .WithMany()  
                .HasForeignKey(j => j.SyncJobId);

            modelBuilder.Entity<JobEmailStatus>()
                .HasOne(j => j.EmailType)
                .WithMany()  
                .HasForeignKey(j => j.EmailTypeId);
        }

        public GMMContext(DbContextOptions<GMMContext> options)
            : base(options)
        {
        }
    }

    public class GMMReadContext : GMMContext
    {
        public GMMReadContext(DbContextOptions<GMMContext> options)
            : base(options)
        {
        }
    }
}