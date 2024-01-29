// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework
{
    public class SyncJobChangeRepository : DatabaseRepositoryBase<SyncJobChange, Entities.SyncJobChange>, ISyncJobChangeRepository
    {
        private readonly GMMContext _writeContext;
        private readonly GMMReadContext _readContext;

        public SyncJobChangeRepository(GMMContext writeContext, GMMReadContext readContext)
        {
            _writeContext = writeContext ?? throw new ArgumentNullException(nameof(writeContext));
            _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        }

        public async Task<IEnumerable<SyncJobChange>> GetSyncJobChangesBySyncJobId(Guid syncJobId)
        {
            return await _readContext.SyncJobChanges
                .Where(s => s.SyncJobId == syncJobId)
                .OrderByDescending(s => s.ChangeTime)
                .Select(s => EntityToModel(s))
                .ToListAsync();
        }

        public async Task<IEnumerable<SyncJobChange>> SaveSyncJobChange(SyncJobChange syncJobChange)
        {
            _writeContext.Set<Entities.SyncJobChange>().Add(ModelToEntity(syncJobChange));
            await _writeContext.SaveChangesAsync();
        }

        protected override SyncJobChange EntityToModel(Entities.SyncJobChange entity)
        {
            return new SyncJobChange
            {
                SyncJobId = entity.SyncJobId,
                ChangeTime = entity.ChangeTime,
                ChangedBy = entity.ChangedBy,
                ChangeSource = (SyncJobChangeSource)entity.ChangeSource,
                ChangeReason = entity.ChangeReason,
                ChangeDetails = entity.ChangeDetails
            };
        }

        protected override Entities.SyncJobChange ModelToEntity(SyncJobChange model)
        {
            return new Entities.SyncJobChange
            {
                SyncJobId = model.SyncJobId,
                ChangeTime = model.ChangeTime,
                ChangedBy = model.ChangedBy,
                ChangeSource = (Entities.SyncJobChangeSource)model.ChangeSource,
                ChangeReason = model.ChangeReason,
                ChangeDetails = model.ChangeDetails
            };
        }
    }
}