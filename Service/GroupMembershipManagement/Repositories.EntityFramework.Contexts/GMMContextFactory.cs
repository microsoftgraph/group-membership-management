// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Repositories.EntityFramework.Contexts
{
    public class GMMContextFactory : IDesignTimeDbContextFactory<GMMContext>
    {
        public GMMContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<GMMContext>();
            optionsBuilder.UseSqlServer(@"<write connection string>");
            return new GMMContext(optionsBuilder.Options);
        }
    }

    public class GMMReadContextFactory : IDesignTimeDbContextFactory<GMMReadContext>
    {
        public GMMReadContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<GMMContext>();
            optionsBuilder.UseSqlServer(@"<read connection string>");
            return new GMMReadContext(optionsBuilder.Options);
        }
    }
}