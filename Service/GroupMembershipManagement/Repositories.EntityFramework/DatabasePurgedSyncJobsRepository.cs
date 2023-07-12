// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;
using System;

namespace Repositories.EntityFramework
{
    public class DatabasePurgedSyncJobsRepository : IDatabasePurgedSyncJobsRepository
    {
        private readonly GMMContext _context;

        public DatabasePurgedSyncJobsRepository(GMMContext gmmContext)
        {
            _context = gmmContext ?? throw new ArgumentNullException(nameof(gmmContext));
        }

        public async Task<int> InsertPurgedSyncJobsAsync(IEnumerable<PurgedSyncJob> jobs)
        {
            foreach (var job in jobs)
            {                
                var entry = _context.Set<PurgedSyncJob>().Add(job);
                entry.State = EntityState.Added;
            }

            return await _context.SaveChangesAsync();
        }

        public async Task<IEnumerable<PurgedSyncJob>> GetPurgedSyncJobsAsync(DateTime cutOffDate)
        { 
            return await _context.PurgedSyncJobs
                                    .Where(job => job.PurgedAt <= cutOffDate)
                                    .ToListAsync();           
        }

        public async Task<int> DeletePurgedSyncJobsAsync(IEnumerable<PurgedSyncJob> jobs)
        {
            foreach (var job in jobs)
            {
                var entry = _context.Set<PurgedSyncJob>().Add(job);
                entry.State = EntityState.Deleted;
            }

            return await _context.SaveChangesAsync();
        }
    }
}