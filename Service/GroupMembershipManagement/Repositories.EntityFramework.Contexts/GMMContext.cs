// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Models;
using System.Text.Json;

namespace Repositories.EntityFramework.Contexts
{
    public class GMMContext : DbContext
    {
        public DbSet<SyncJob> SyncJobs { get; set; } = null!;
        public DbSet<PurgedSyncJob> PurgedSyncJobs { get; set; } = null!;
        public DbSet<Status> Statuses { get; set; } = null!;
        public DbSet<Setting> Settings { get; set; } = null!;
        public DbSet<Entities.SqlMembershipSource> SqlMembershipSources { get; set; } = null!;
        public DbSet<NotificationType> NotificationTypes { get; set; }
        public DbSet<JobNotification> JobNotifications { get; set; }
        public DbSet<DestinationName> DestinationNames { get; set; }
        public DbSet<DestinationOwner> DestinationOwners { get; set; }
        public DbSet<Entities.SyncJobChange> SyncJobChanges { get; set; } = null!;
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

            modelBuilder.Entity<Entities.SqlMembershipSource>(source =>
            {
                source.HasKey(s => s.Id);
                source.Property(s => s.Id)
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("NEWSEQUENTIALID()");

                source.Property(s => s.Name)
                    .IsRequired();
                source.HasIndex(s => s.Name).IsUnique();

                source.Property(e => e.Attributes)
                    .HasConversion(
                        v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                        v => string.IsNullOrEmpty(v) ? null : JsonSerializer.Deserialize<List<SqlMembershipAttribute>>(v, (JsonSerializerOptions?)null),
                        new ValueComparer<List<SqlMembershipAttribute>>(
                        (c1, c2) =>
                            (c1 == null && c2 == null) ||
                            (c1 != null && c2 != null && JsonSerializer.Serialize(c1, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(c2, (JsonSerializerOptions?)null)),
                        c => c == null ? 0 : JsonSerializer.Serialize(c, (JsonSerializerOptions?)null).GetHashCode(),
                        c => c == null ? null : JsonSerializer.Deserialize<List<SqlMembershipAttribute>>(JsonSerializer.Serialize(c, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null))
                    );

                source.HasData(new Entities.SqlMembershipSource
                {
                    Id = Guid.NewGuid(),
                    Name = "SqlMembership",
                    CustomLabel = null,
                    Attributes = null
                });
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

            modelBuilder.Entity<Entities.SyncJobChange>(entity =>
            {
                // Keys
                entity.HasKey(s => s.Id);

                // Indexes
                entity.HasIndex(s => s.SyncJobId);
                entity.HasIndex(s => s.ChangeTime);
                entity.HasIndex(s => s.ChangedByObjectId);
                entity.HasIndex(s => s.ChangeSource);

                // Properties
                entity.Property(s => s.Id).ValueGeneratedOnAdd().HasDefaultValueSql("NEWSEQUENTIALID()");
                entity.Property(s => s.SyncJobId).IsRequired();
                entity.Property(s => s.ChangeTime).IsRequired().HasDefaultValue(DateTime.UtcNow);
                entity.Property(s => s.ChangedByDisplayName).IsRequired();
                entity.Property(s => s.ChangedByObjectId).IsRequired();
                entity.Property(s => s.ChangeSource).IsRequired();
                entity.Property(s => s.ChangeReason).IsRequired();
                entity.Property(s => s.ChangeDetails).IsRequired();
            });

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