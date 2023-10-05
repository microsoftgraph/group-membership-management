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
        public DbSet<Setting> Settings { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SyncJob>().Property(t => t.Id)
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("NEWSEQUENTIALID()");

            modelBuilder.Entity<PurgedSyncJob>().Property(p => p.Id)
                  .ValueGeneratedOnAdd()
                  .HasDefaultValueSql("NEWID()");

            modelBuilder.Entity<Setting>().HasKey(s => s.Key);
        }

        public GMMContext(DbContextOptions options)
            : base(options)
        {
        }
    }
    public class GMMWriteContext : GMMContext
    {
        public GMMWriteContext(DbContextOptions<GMMWriteContext> options)
            : base(options)
        {
        }
    }
    public class GMMReadContext : GMMContext
    {
        public GMMReadContext(DbContextOptions<GMMReadContext> options)
            : base(options)
        {
        }
    }
}