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
        public Guid RunId { get; set; }
        Task<List<SyncJob>> GetSyncJobsAsync(SyncStatus syncStatus);
        Task<string> GetGroupNameAsync(Guid groupId);
        Task SendEmailAsync(SyncJob job, string emailSubjectTemplateName, string emailContentTemplateName, string[] additionalContentParameters);
        Task<bool> GroupExistsAndGMMCanWriteToGroupAsync(SyncJob job);
        Task UpdateSyncJobStatusAsync(SyncStatus status, SyncJob job);
        Task SendMessageAsync(SyncJob job);
        Task<List<string>> GetGroupEndpointsAsync(Guid groupId);
    }
}
