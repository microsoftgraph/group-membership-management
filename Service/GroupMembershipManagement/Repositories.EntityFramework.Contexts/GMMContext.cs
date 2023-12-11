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
        public DbSet<NotificationType> NotificationTypes { get; set; }
        public DbSet<JobNotification> JobNotifications { get; set; }
        public DbSet<DestinationName> DestinationNames { get; set; }
        public DbSet<DestinationOwner> DestinationOwners { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SyncJob>().Property(t => t.Id)
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("NEWSEQUENTIALID()");

            modelBuilder.Entity<PurgedSyncJob>().Property(p => p.Id)
                  .ValueGeneratedOnAdd()
                  .HasDefaultValueSql("NEWID()");

            modelBuilder.Entity<Setting>(entity =>
            {
                entity.HasKey(s => s.Id); 
                entity.Property(s => s.SettingKey)
                    .IsRequired()
                    .HasConversion(
                        v => v.ToString(),
                        v => (SettingKey)Enum.Parse(typeof(SettingKey), v));
                entity.HasIndex(s => s.SettingKey).IsUnique();
                entity.Property(s => s.SettingValue);
            });

            modelBuilder.Entity<SyncJob>()
                        .HasOne(s => s.StatusDetails)
                        .WithOne()
                        .HasForeignKey<SyncJob>(x => x.Status)
                        .HasPrincipalKey<Status>(x => x.Name)
                        .IsRequired(false)
                        .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Status>()
                        .ToTable("Statuses");

            modelBuilder.Entity<JobNotification>().Property(t => t.Id)
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("NEWSEQUENTIALID()");

            modelBuilder.Entity<JobNotification>()
                .HasOne(j => j.SyncJob)
                .WithMany()  
                .HasForeignKey(j => j.SyncJobId)
                .OnDelete(DeleteBehavior.Cascade);
            
            modelBuilder.Entity<JobNotification>()
                .HasIndex(j => new { j.SyncJobId, j.NotificationTypeID })
                .IsUnique();
                
            modelBuilder.Entity<SyncJob>()
                .HasOne(syncJob => syncJob.DestinationName)
                .WithOne(name => name.SyncJob)
                .HasForeignKey<DestinationName>(name => name.Id);

            modelBuilder.Entity<SyncJob>()
                .HasMany(syncJob => syncJob.DestinationOwners)
                .WithMany(owner => owner.SyncJobs);

            modelBuilder.Entity<DestinationOwner>().Property(owner => owner.Id)
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("NEWSEQUENTIALID()");

            modelBuilder.Entity<DestinationOwner>()
                .HasIndex(owner => owner.ObjectId);

            modelBuilder.Entity<DestinationName>()
                .HasIndex(name => name.Name);

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