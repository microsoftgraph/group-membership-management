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
            optionsBuilder.UseSqlServer("Server=tcp:gmm-data-yy.database.windows.net,1433;Initial Catalog=gmm-data-yy-jobs;Authentication=Active Directory Default;Connection Timeout=110;");
            return new GMMContext(optionsBuilder.Options);
        }
    }

    public class GMMReadContextFactory : IDesignTimeDbContextFactory<GMMReadContext>
    {
        public GMMReadContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<GMMContext>();
            optionsBuilder.UseSqlServer("Server=tcp:gmm-data-yy-r.database.windows.net,1433;Initial Catalog=gmm-data-yy-jobs-R;Authentication=Active Directory Default;Connection Timeout=110;");
            return new GMMReadContext(optionsBuilder.Options);
        }
    }
}