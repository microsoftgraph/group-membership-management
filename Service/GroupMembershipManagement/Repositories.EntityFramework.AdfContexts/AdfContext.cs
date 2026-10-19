// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models.AdfRunHistory;
using System;

namespace Repositories.EntityFramework.AdfContexts
{
    /// <summary>
    /// Entity Framework context for the ADF database, separate from <c>GMMContext</c> (the Jobs database).
    /// </summary>
    public class AdfContext : DbContext
    {
        public AdfContext(DbContextOptions<AdfContext> options)
            : base(options)
        {
        }

        public DbSet<AdfRunHistory> AdfRunHistory { get; set; } = null!;

        /// <summary>
        /// Maps to the SQL Server built-in <c>OBJECT_ID</c> so queries can check whether a table exists.
        /// </summary>
        public static int? ObjectId(string objectName, string objectType)
            => throw new NotSupportedException("Translated to OBJECT_ID by Entity Framework.");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDbFunction(() => ObjectId(default!, default!))
                .HasName("OBJECT_ID")
                .IsBuiltIn();

            modelBuilder.Entity<AdfRunHistory>(entity =>
            {
                entity.HasKey(run => run.AdfRunId);
                entity.Property(run => run.AdfRunId)
                    .HasColumnName("AdfRunId")
                    .ValueGeneratedNever();
                entity.Property(run => run.PublishedAtUtc)
                    .HasColumnName("DateCreated")
                    .HasColumnType("datetime")
                    .IsRequired();
                entity.HasIndex(run => new { run.PublishedAtUtc, run.AdfRunId });
                entity.ToTable("AdfRunHistory", "dbo");
            });
        }
    }
}
