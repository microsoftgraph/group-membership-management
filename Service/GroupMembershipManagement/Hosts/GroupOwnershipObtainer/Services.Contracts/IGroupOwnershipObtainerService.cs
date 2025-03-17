// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Services.Entities;

namespace Services.Contracts
{
    public interface IGroupOwnershipObtainerService
    {
        public Guid RunId { get; set; }
        Task<Guid> GetGroupIdAsync(SyncJob syncJob);
        Task<List<SyncJob>> GetSyncJobsSegmentAsync();
        public Task<List<Guid>> GetGroupOwnersAsync(Guid groupId);
        public Task<string> SendMembershipAsync(SyncJob syncJob, Guid groupId, List<Guid> allusers, int currentPart, bool exclusionary);
        public List<Guid> FilterSyncJobsBySourceTypes(HashSet<string> requestedSourceTypes, List<JobsFilterSyncJob> syncJobs);
    }
}