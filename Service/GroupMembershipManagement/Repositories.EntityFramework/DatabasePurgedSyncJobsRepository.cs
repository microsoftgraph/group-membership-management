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
        private readonly GMMWriteContext _writeContext;
        private readonly GMMReadContext _readContext;

        public DatabasePurgedSyncJobsRepository(GMMWriteContext writeContext, GMMReadContext readContext)
        {
            _writeContext = writeContext ?? throw new ArgumentNullException(nameof(writeContext));
            _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        }

        public async Task<int> InsertPurgedSyncJobsAsync(IEnumerable<PurgedSyncJob> jobs)
        {
            foreach (var job in jobs)
            {                
                var entry = _writeContext.Set<PurgedSyncJob>().Add(job);
                entry.State = EntityState.Added;
            }

            return await _writeContext.SaveChangesAsync();
        }

        public async Task<IEnumerable<PurgedSyncJob>> GetPurgedSyncJobsAsync(DateTime cutOffDate)
        { 
            return await _readContext.PurgedSyncJobs
                                    .Where(job => job.PurgedAt <= cutOffDate)
                                    .ToListAsync();           
        }

        public async Task<int> DeletePurgedSyncJobsAsync(IEnumerable<PurgedSyncJob> jobs)
        {
            foreach (var job in jobs)
            {
                var entry = _writeContext.Set<PurgedSyncJob>().Add(job);
                entry.State = EntityState.Deleted;
            }

            return await _writeContext.SaveChangesAsync();
        }
    }
}