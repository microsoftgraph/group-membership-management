// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IJobTriggerService
    {
        Task<List<SyncJob>> GetSyncJobsAsync();
        Task<string> GetGroupNameAsync(Guid groupId);
        Task SendEmailAsync(SyncJob job, string groupName);
        Task<bool> CanWriteToGroup(SyncJob job);
        Task UpdateSyncJobStatusAsync(SyncStatus status, SyncJob job);
        Task SendMessageAsync(SyncJob job);
    }
}
