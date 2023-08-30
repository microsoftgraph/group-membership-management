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
        public DbSet<Configuration> Configuration { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SyncJob>().Property(t => t.Id)
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("NEWSEQUENTIALID()");

            modelBuilder.Entity<PurgedSyncJob>().Property(p => p.Id)
                  .ValueGeneratedOnAdd()
                  .HasDefaultValueSql("NEWID()");

            modelBuilder.Entity<Configuration>();
        }

        public GMMContext(DbContextOptions<GMMContext> options)
            : base(options)
        {
        }
    }
}