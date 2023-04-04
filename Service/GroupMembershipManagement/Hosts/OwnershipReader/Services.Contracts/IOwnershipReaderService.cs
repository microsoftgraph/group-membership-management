// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Entities;
using Services.Entities;

namespace Services.Contracts
{
    public interface IOwnershipReaderService
    {
        public Guid RunId { get; set; }
        public Task<TableSegmentBulkResult<SyncJob>> GetSyncJobsSegmentAsync(AsyncPageable<SyncJob> pageableQueryResult, string continuationToken);
        public Task<List<Guid>> GetGroupOwnersAsync(Guid groupId);
        public Task<string> SendMembershipAsync(SyncJob syncJob, List<Guid> allusers, int currentPart, bool exclusionary);
        public List<Guid> FilterSyncJobsBySourceTypes(HashSet<string> requestedSourceTypes, List<JobsFilterSyncJob> syncJobs);
    }
}