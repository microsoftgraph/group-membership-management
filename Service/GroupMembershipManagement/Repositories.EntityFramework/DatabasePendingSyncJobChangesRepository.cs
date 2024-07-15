// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;
using System;

namespace Repositories.EntityFramework
{
    public class DatabasePendingSyncJobChangesRepository : IDatabasePendingSyncJobChangesRepository
    {
        private readonly GMMContext _writeContext;
        private readonly GMMReadContext _readContext;

        public DatabasePendingSyncJobChangesRepository(GMMContext writeContext, GMMReadContext readContext)
        {
            _writeContext = writeContext ?? throw new ArgumentNullException(nameof(writeContext));
            _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        }

        public async Task<Guid> InsertPendingSyncJobChangeAsync(PendingSyncJobChange pendingSyncJobChange)
        {
            var entry = await _writeContext.Set<PendingSyncJobChange>().AddAsync(pendingSyncJobChange);
            await _writeContext.SaveChangesAsync();
            return entry.Entity.Id;
        }

        public async Task<IEnumerable<PendingSyncJobChange>> GetAllPendingSyncJobChangesAsync()
        { 
            return await _readContext.PendingSyncJobChanges
                                    .ToListAsync();           
        }

        public async Task<IEnumerable<PendingSyncJobChange>> GetActivePendingSyncJobChangesAsync()
        { 
            return await _readContext.PendingSyncJobChanges
                                    .Where(job => job.Status != ReviewStatus.SubmissionRejected)
                                    .ToListAsync();           
        }
        
        public async Task<PendingSyncJobChange> GetPendingSyncJobChangeByObjectIdAsync(Guid objectId)
        {
            return await _readContext.PendingSyncJobChanges.FromSqlRaw<PendingSyncJobChange>(@"SELECT * FROM [dbo].[PendingSyncJobChanges] WHERE JSON_VALUE(Destination, '$[0].value.objectId') = {0}", objectId.ToString()).FirstOrDefaultAsync();
        }
    }
}