// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Models;
using Models.AdfRun;
using Models.Notifications;
using Models.SyncJobHistory;
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
        public DbSet<DestinationEmail> DestinationEmail { get; set; }
        public DbSet<Entities.SyncJobChange> SyncJobChanges { get; set; } = null!;
        public DbSet<SyncJobHistory> SyncJobHistory { get; set; } = null!;
        public DbSet<AdfRun> AdfRuns { get; set; } = null!;
        public DbSet<ThresholdNotification> ThresholdNotifications { get; set; } = null!;
        public DbSet<ServiceStatus> ServiceStatus { get; set; }
        public DbSet<ServiceStatusHistory> ServiceStatusHistory { get; set; }
        public DbSet<MembershipType> MembershipTypes { get; set; }
        public DbSet<Group> Groups { get; set; }
        public DbSet<Channel> TeamsChannels { get; set; }
        public DbSet<Title> Titles { get; set; }
        public DbSet<DeferredNotification> DeferredNotifications { get; set; }

        // Consolidated Table-Per-Type (TPT) destination model. These sets are shared by
        // GMMReadContext (which derives from GMMContext) so read and write models stay identical.
        public DbSet<Destination> Destinations { get; set; }
        public DbSet<GroupDestination> GroupDestinations { get; set; }
        public DbSet<TeamsChannelDestination> TeamsChannelDestinations { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // EF Core 9+ promotes PendingModelChangesWarning to an error thrown by Database.Migrate().
            // GMMContext seeds non-deterministic values (e.g. Guid.NewGuid() and DateTime.UtcNow), so the
            // model differs on every build and trips this check even though no schema/migration change is
            // intended. Ignore the warning to preserve the pre-.NET 10 (EF Core 8) migrate-at-startup behavior.
            optionsBuilder.ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        }

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

            modelBuilder.Entity<SyncJob>()
                        .Property(s => s.InitialOnboardingDate)
                        .ValueGeneratedOnAdd()
                        .HasDefaultValueSql("GETUTCDATE()");

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
                .HasOne(syncJob => syncJob.DestinationEmail)
                .WithOne(email => email.SyncJob)
                .HasForeignKey<DestinationEmail>(email => email.Id);

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

            modelBuilder.Entity<DestinationEmail>()
                .HasIndex(name => name.Email);

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
                entity.Property(s => s.ChangeReason).IsRequired();
            });

            modelBuilder.Entity<SyncJobHistory>(entity =>
            {
                entity.HasKey(h => h.Id);
                entity.Property(h => h.Id).ValueGeneratedOnAdd().HasDefaultValueSql("NEWSEQUENTIALID()");
                entity.Property(h => h.SyncJobId).IsRequired();
                entity.Property(h => h.RunId).IsRequired();
                entity.Property(h => h.StartTime);
                entity.Property(h => h.EndTime);
                entity.Property(h => h.Duration);
                entity.Property(h => h.Status);
                entity.Property(h => h.UsersAdded);
                entity.Property(h => h.UsersRemoved);
                entity.Property(h => h.BeforeSyncUserCount);
                entity.Property(h => h.AfterSyncUserCount);
                entity.Property(h => h.AdfRunId);
                entity.Property(h => h.UpdatedByFunction).HasMaxLength(255);
                entity.Property(h => h.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(h => h.UpdatedAt)
                    .ValueGeneratedOnAddOrUpdate()
                    .HasDefaultValueSql("GETUTCDATE()");

                entity.HasIndex(h => h.SyncJobId);
                entity.HasIndex(h => h.RunId).IsUnique();
                entity.HasIndex(h => h.EndTime);

                entity.ToTable("SyncJobHistory");
            });

            modelBuilder.Entity<AdfRun>(entity =>
            {
                entity.HasKey(a => a.Id);
                entity.Property(a => a.Id).ValueGeneratedOnAdd().HasDefaultValueSql("NEWSEQUENTIALID()");
                entity.Property(a => a.AdfRunId).IsRequired().HasMaxLength(255);
                entity.Property(a => a.Notes).HasMaxLength(2000);
                entity.Property(a => a.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(a => a.NotesModifiedAt);

                entity.HasIndex(a => a.AdfRunId).IsUnique();

                entity.ToTable("AdfRuns");
            });

            modelBuilder.Entity<DeferredNotification>(entity =>
            {
                entity.HasKey(d => d.Id);
                entity.Property(d => d.Id).ValueGeneratedOnAdd();
                entity.Property(d => d.SequenceNumber).IsRequired();
                entity.Property(d => d.MessageType).IsRequired()
                    .HasConversion(
                        v => v.ToString(),
                        v => (NotificationMessageType)Enum.Parse(typeof(NotificationMessageType), v))
                    .IsUnicode(false)
                    .HasMaxLength(100);
                entity.Property(d => d.Status).IsRequired()
                    .HasConversion(
                        v => v.ToString(),
                        v => (DeferredNotificationStatus)Enum.Parse(typeof(DeferredNotificationStatus), v))
                    .IsUnicode(false)
                    .HasMaxLength(50);
                entity.Property(d => d.DeferredAt).IsRequired();
                entity.Property(d => d.SuppressionReason).HasMaxLength(500);

                entity.HasIndex(d => d.SequenceNumber).IsUnique();
                entity.HasIndex(d => new { d.MessageType, d.Status });

                entity.ToTable("DeferredNotifications");
            });

            modelBuilder.Entity<ThresholdNotification>(entity =>
            {
                entity.HasKey(t => t.Id);

                entity.Property(t => t.Id)
                    .ValueGeneratedOnAdd()
                    .HasDefaultValueSql("NEWSEQUENTIALID()");

                entity.Property(t => t.TargetOfficeGroupId).IsRequired();
                entity.Property(t => t.SyncJobId).IsRequired();
                entity.Property(t => t.StatusName).HasMaxLength(50);
                entity.Property(t => t.ResolvedBy).HasMaxLength(255);
                entity.Property(t => t.ResolutionName).HasMaxLength(50);
                entity.Property(t => t.CardStateName).HasMaxLength(50);

                entity.Property(t => t.ThresholdPercentageForAdditions).HasDefaultValue(100);
                entity.Property(t => t.ThresholdPercentageForRemovals).HasDefaultValue(20);
                entity.Property(t => t.ChangePercentageForAdditions).HasDefaultValue(0);
                entity.Property(t => t.ChangePercentageForRemovals).HasDefaultValue(0);
                entity.Property(t => t.ChangeQuantityForAdditions).HasDefaultValue(0);
                entity.Property(t => t.ChangeQuantityForRemovals).HasDefaultValue(0);
                entity.Property(t => t.CreatedTime).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(t => t.ResolvedTime).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(t => t.LastUpdatedTime)
                    .ValueGeneratedOnAddOrUpdate()
                    .HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne<SyncJob>()
                    .WithMany()
                    .HasForeignKey(t => t.SyncJobId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasPrincipalKey(s => s.Id);  
            });

            modelBuilder.Entity<NotificationType>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Name)
                    .HasConversion(
                        v => v.ToString(),
                        v => (NotificationMessageType)Enum.Parse(typeof(NotificationMessageType), v))
                    .IsUnicode(false);
            });
            SeedNotificationTypes(modelBuilder);


            modelBuilder.Entity<ServiceStatus>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name)
                    .HasConversion(
                        v => v.ToString(),
                        v => (ServiceStatuses)Enum.Parse(typeof(ServiceStatuses), v))
                    .IsUnicode(false);
                entity.ToTable("ServiceStatuses");
            });

            modelBuilder.Entity<MembershipType>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name)
                    .HasConversion(
                        v => v.ToString(),
                        v => (MembershipTypes)Enum.Parse(typeof(MembershipTypes), v))
                    .IsUnicode(false);
                entity.ToTable("MembershipTypes");
            });

            modelBuilder.Entity<Group>()
                .HasKey(g => g.SyncJobId);

            modelBuilder.Entity<Group>()
                .HasIndex(g => new { g.SyncJobId, g.GroupId })
                .IsUnique();

            modelBuilder.Entity<Channel>()
                .HasKey(g => g.SyncJobId);

            modelBuilder.Entity<Channel>()
                .HasIndex(g => new { g.SyncJobId, g.GroupId })
                .IsUnique();

            ConfigureConsolidatedDestinations(modelBuilder);
        }

        /// <summary>
        /// Maps the consolidated TPT destination model (base <c>Destinations</c> plus shared-key
        /// <c>GroupDestinations</c>/<c>TeamsChannelDestinations</c>) for both write and read contexts.
        /// </summary>
        private static void ConfigureConsolidatedDestinations(ModelBuilder modelBuilder)
        {
            // Base destination row: one per supported SyncJob, typed via MembershipTypes.
            modelBuilder.Entity<Destination>(entity =>
            {
                entity.ToTable("Destinations");

                entity.HasKey(d => d.SyncJobId);

                // SyncJobId is both PK and FK to SyncJobs.Id; cascade with the owning job.
                entity.HasOne<SyncJob>()
                    .WithOne()
                    .HasForeignKey<Destination>(d => d.SyncJobId)
                    .HasPrincipalKey<SyncJob>(s => s.Id)
                    .OnDelete(DeleteBehavior.Cascade);

                // Type discriminator; DB-level FK to MembershipTypes.Name (SyncJobs.MembershipType standard, not by Id).
                entity.Property(d => d.DestinationType)
                    .IsRequired()
                    .HasMaxLength(MembershipTypeNameMaxLength);
                entity.HasIndex(d => d.DestinationType);
            });

            // Group per-type row: shared PK/FK to the base destination.
            modelBuilder.Entity<GroupDestination>(entity =>
            {
                entity.ToTable("GroupDestinations");

                entity.HasKey(g => g.SyncJobId);

                entity.HasOne<Destination>()
                    .WithOne()
                    .HasForeignKey<GroupDestination>(g => g.SyncJobId)
                    .HasPrincipalKey<Destination>(d => d.SyncJobId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.Property(g => g.GroupId).IsRequired();
                entity.Property(g => g.Name);
                entity.Property(g => g.Email);

                entity.HasIndex(g => g.GroupId);
            });

            // Teams-channel per-type row: shared PK/FK plus required channel identity.
            modelBuilder.Entity<TeamsChannelDestination>(entity =>
            {
                entity.ToTable("TeamsChannelDestinations");

                entity.HasKey(t => t.SyncJobId);

                entity.HasOne<Destination>()
                    .WithOne()
                    .HasForeignKey<TeamsChannelDestination>(t => t.SyncJobId)
                    .HasPrincipalKey<Destination>(d => d.SyncJobId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.Property(t => t.TeamId).IsRequired();

                // Preserve the legacy TeamsChannels.ChannelId capacity (nvarchar(255)).
                entity.Property(t => t.ChannelId)
                    .IsRequired()
                    .HasMaxLength(TeamsChannelDestinationChannelIdMaxLength);

                entity.Property(t => t.TeamName);
                entity.Property(t => t.ChannelName);

                entity.HasIndex(t => t.TeamId);
            });
        }

        // Preserve the legacy TeamsChannels.ChannelId capacity (nvarchar(255)) so values aren't truncated.
        internal const int TeamsChannelDestinationChannelIdMaxLength = 255;

        // Matches the MembershipTypes.Name capacity so the by-Name FK column aligns with its principal.
        internal const int MembershipTypeNameMaxLength = 255;
        private void SeedNotificationTypes(ModelBuilder modelBuilder)
        {
            var notificationTypes = Enum.GetValues(typeof(NotificationMessageType))
                .Cast<NotificationMessageType>()
                .Select((value, index) => new NotificationType
                {
                    Id = index + 1,
                    Name = value,
                    Disabled = false
                });

            modelBuilder.Entity<NotificationType>().HasData(notificationTypes);
        }

        public GMMContext(DbContextOptions<GMMContext> options)
            : base(options)
        {
        }
    }

    /// <summary>
    /// Read-optimized context for resolver/query projections. It derives from
    /// <see cref="GMMContext"/> so every table, key, relationship, and property mapping —
    /// including the consolidated TPT destination model (<c>Destinations</c>,
    /// <c>GroupDestinations</c>, <c>TeamsChannelDestinations</c>) — is byte-for-byte
    /// identical to the write context. Read and write mappings therefore cannot drift;
    /// consolidated reads use no-tracking typed projections (e.g. <c>AsNoTracking()</c>)
    /// over these shared sets. Writes never use this context.
    /// </summary>
    public class GMMReadContext : GMMContext
    {
        public GMMReadContext(DbContextOptions<GMMContext> options)
            : base(options)
        {
        }
    }
}