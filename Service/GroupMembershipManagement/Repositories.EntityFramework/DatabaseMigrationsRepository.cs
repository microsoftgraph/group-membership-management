// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework
{
    public class DatabaseMigrationsRepository : IDatabaseMigrationsRepository
    {
        private readonly GMMContext _writeContext;

        public DatabaseMigrationsRepository(GMMContext writeContext)
        {
            _writeContext = writeContext ?? throw new ArgumentNullException(nameof(writeContext));
        }

        public async Task MigrateDatabaseAsync()
        {
            var pendingMigrations = await _writeContext.Database.GetPendingMigrationsAsync();
            if(pendingMigrations.Any())
                await _writeContext.Database.MigrateAsync();
        }
    }
}
