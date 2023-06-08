// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework
{
    public class DatabaseSyncJobRepository : IDatabaseSyncJobRepository
    {
        private readonly GMMContext _context;
        private readonly DbContextOptions<GMMContext> _contextOptions;

        public DatabaseSyncJobRepository(GMMContext gmmContext, DbContextOptions<GMMContext> gmmContextOptions)
        {
            _context = gmmContext ?? throw new ArgumentNullException(nameof(gmmContext));
            _contextOptions = gmmContextOptions ?? throw new ArgumentNullException(nameof(gmmContextOptions));
        }

        public Task<List<SyncJob>> SelectSyncJobsAsync()
        {
            return _context.SyncJobs.ToListAsync();
        }
    }
}
