// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Repositories.EntityFramework.AdfContexts
{
    /// <summary>
    /// Design-time factory for EF tooling (migrations). The placeholder connection string is only
    /// used for model discovery.
    /// </summary>
    public class AdfContextFactory : IDesignTimeDbContextFactory<AdfContext>
    {
        public AdfContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<AdfContext>();
            optionsBuilder.UseSqlServer(@"<adf connection string>", sqlServerOptions =>
            {
                sqlServerOptions.EnableRetryOnFailure();
            });
            return new AdfContext(optionsBuilder.Options);
        }
    }
}
