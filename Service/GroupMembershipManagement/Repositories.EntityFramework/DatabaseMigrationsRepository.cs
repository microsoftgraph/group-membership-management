// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework
{
    public class DatabaseMigrationsRepository : IDatabaseMigrationsRepository
    {
        private readonly GMMContext _context;
        private readonly DbContextOptions<GMMContext> _contextOptions;

        public DatabaseMigrationsRepository(GMMContext gmmContext, DbContextOptions<GMMContext> gmmContextOptions)
        {
            _context = gmmContext ?? throw new ArgumentNullException(nameof(gmmContext));
            _contextOptions = gmmContextOptions ?? throw new ArgumentNullException(nameof(gmmContextOptions));
        }

        public async Task MigrateDatabaseAsync()
        {
            var pendingMigrations = await _context.Database.GetPendingMigrationsAsync();
            if(pendingMigrations.Any())
                await _context.Database.MigrateAsync();
        }
    }
}
