// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Repositories.EntityFramework.Contexts
{
    public class GMMWriteContextFactory : IDesignTimeDbContextFactory<GMMWriteContext>
    {
        public GMMWriteContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<GMMWriteContext>();
            optionsBuilder.UseSqlServer(@"<write connection string>");
            return new GMMWriteContext(optionsBuilder.Options);
        }
    }

    public class GMMReadContextFactory : IDesignTimeDbContextFactory<GMMReadContext>
    {
        public GMMReadContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<GMMReadContext>();
            optionsBuilder.UseSqlServer(@"<read connection string>");
            return new GMMReadContext(optionsBuilder.Options);
        }
    }
}