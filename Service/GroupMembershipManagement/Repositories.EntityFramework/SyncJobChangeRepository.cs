// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Models.SyncJobChange;
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

        public async Task<RepositoryPage<SyncJobChange>> GetPageOfSyncJobChangesBySyncJobId(
            Guid syncJobId,
            int startPage = 1,
            int pageSize = 10,
            SyncJobChangeSortingField sortBy = SyncJobChangeSortingField.ChangeTime,
            bool sortAscending = false)
        {

            IQueryable listOppLineData = Enumerable.Empty<string>().AsQueryable();

            // filtering
            var query = _readContext.SyncJobChanges
                .Where(s => s.SyncJobId == syncJobId);

            // count
            var pagingQueryable = query.AsQueryable();
            var totalCount = await pagingQueryable.CountAsync();

            // sorting
            _ = sortBy switch
            {
                SyncJobChangeSortingField.ChangeTime => query = sortAscending
                    ? query.OrderBy(s => s.ChangeTime)
                    : query.OrderByDescending(s => s.ChangeTime),
                SyncJobChangeSortingField.ChangedBy => query = sortAscending
                    ? query.OrderBy(s => s.ChangedBy)
                    : query.OrderByDescending(s => s.ChangedBy),
                SyncJobChangeSortingField.ChangeSource => query = sortAscending
                    ? query.OrderBy(s => s.ChangeSource)
                    : query.OrderByDescending(s => s.ChangeSource),
                SyncJobChangeSortingField.ChangeReason => query = sortAscending
                    ? query.OrderBy(s => s.ChangeReason)
                    : query.OrderByDescending(s => s.ChangeReason),
                SyncJobChangeSortingField.ChangeDetails => query = sortAscending
                    ? query.OrderBy(s => s.ChangeDetails)
                    : query.OrderByDescending(s => s.ChangeDetails),
                _ => query = query.OrderByDescending(s => s.ChangeTime)
            };

            // paging
            var skip = (startPage - 1) * pageSize;
            var take = Math.Min(pageSize, totalCount - skip); // take a page size or the remaining items, whichever is smaller
            query = query.Skip(skip).Take(take);

            var totalPages = totalCount % pageSize == 0 ? totalCount / pageSize : (totalCount / pageSize) + 1;


            // transformation and execution
            var items = await query.Select(s => EntityToModel(s)).ToListAsync();
            return new RepositoryPage<SyncJobChange>
            {
                Items = items,
                PageNumber = startPage,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages
            };
        }

        public async Task SaveSyncJobChange(SyncJobChange syncJobChange)
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