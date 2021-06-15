// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface ISyncJobTopicService
    {
        Task<List<SyncJob>> GetSyncJobsAsync();
        Task<string> GetGroupNameAsync(Guid groupId);
        Task SendEmailAsync(SyncJob job, string groupName);
        //Task ProcessSyncJobsAsync(List<SyncJob> jobs);
        Task<bool> CanWriteToGroup(SyncJob job);
        Task UpdateSyncJobStatusAsync(bool canWriteToGroup, SyncJob job);
        Task SendMessageAsync(SyncJob job);
    }
}
