// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework
{
    public class DatabaseMigrationsRepository : IDatabaseMigrationsRepository
    {
        private readonly GMMWriteContext _writeContext;
        private readonly DbContextOptions<GMMContext> _contextOptions;

        public DatabaseMigrationsRepository(GMMWriteContext writeContext, DbContextOptions<GMMContext> gmmContextOptions)
        {
            _writeContext = writeContext ?? throw new ArgumentNullException(nameof(writeContext));
            _contextOptions = gmmContextOptions ?? throw new ArgumentNullException(nameof(gmmContextOptions));
        }

        public async Task MigrateDatabaseAsync()
        {
            var pendingMigrations = await _writeContext.Database.GetPendingMigrationsAsync();
            if(pendingMigrations.Any())
                await _writeContext.Database.MigrateAsync();
        }
    }
}
