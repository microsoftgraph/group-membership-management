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
        private readonly GMMContext _writeContext;
        private readonly GMMReadContext _readContext;

        public DatabasePurgedSyncJobsRepository(GMMContext writeContext, GMMReadContext readContext)
        {
            _writeContext = writeContext ?? throw new ArgumentNullException(nameof(writeContext));
            _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        }

        public async Task<int> InsertPurgedSyncJobsAsync(IEnumerable<PurgedSyncJob> jobs)
        {
            var jobsToAdd = new List<PurgedSyncJob>();

            foreach (var job in jobs)
            {
                var existingJob = await _writeContext.Set<PurgedSyncJob>()
                                                      .AnyAsync(j => j.TargetOfficeGroupId == job.TargetOfficeGroupId);

                if (!existingJob)
                {
                    jobsToAdd.Add(job);
                }
            }

            if (jobsToAdd.Any())
            {
                await _writeContext.Set<PurgedSyncJob>().AddRangeAsync(jobsToAdd);
                return await _writeContext.SaveChangesAsync();
            }

            return 0;
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
                var entry = await _writeContext.Set<PurgedSyncJob>().AddAsync(job);
                entry.State = EntityState.Deleted;
            }

            return await _writeContext.SaveChangesAsync();
        }
    }
}