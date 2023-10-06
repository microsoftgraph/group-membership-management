// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;

namespace Repositories.EntityFramework.Contexts
{
    public class GMMContext : DbContext
    {
        public DbSet<SyncJob> SyncJobs { get; set; }
        public DbSet<PurgedSyncJob> PurgedSyncJobs { get; set; }
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

            modelBuilder.Entity<JobEmailStatus>().Property(t => t.JobEmailStatusId)
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("NEWSEQUENTIALID()");

            modelBuilder.Entity<EmailType>().HasData(
                    new EmailType { EmailTypeId = 1, EmailTypeName = "OnBoarding" }
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
}