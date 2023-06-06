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
            optionsBuilder.UseSqlServer(@"<connection string>");
            return new GMMContext(optionsBuilder.Options);
        }
    }
}