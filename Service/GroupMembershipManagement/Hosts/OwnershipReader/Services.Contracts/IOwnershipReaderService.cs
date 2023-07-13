// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Services.Entities;

namespace Services.Contracts
{
    public interface IOwnershipReaderService
    {
        public Guid RunId { get; set; }
        Task<List<SyncJob>> GetSyncJobsSegmentAsync();
        public Task<List<Guid>> GetGroupOwnersAsync(Guid groupId);
        public Task<string> SendMembershipAsync(SyncJob syncJob, List<Guid> allusers, int currentPart, bool exclusionary);
        public List<Guid> FilterSyncJobsBySourceTypes(HashSet<string> requestedSourceTypes, List<JobsFilterSyncJob> syncJobs);
    }
}