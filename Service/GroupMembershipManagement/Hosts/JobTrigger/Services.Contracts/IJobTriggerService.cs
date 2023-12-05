// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IJobTriggerService
    {
        public Guid RunId { get; set; }
        Task<(List<SyncJob> jobs, bool jobTriggerThresholdExceeded, int maxJobAllowed)> GetSyncJobsAsync();
        Task<(bool IsValid, AzureADGroup DestinationObject)> ParseAndValidateDestinationAsync(SyncJob syncJob);
        Task<AzureADGroup> ParseDestinationAsync(SyncJob syncJob);
        Task<string> GetGroupNameAsync(SyncJob job);
        Task SendEmailAsync(SyncJob job, string emailSubjectTemplateName, string emailContentTemplateName, string[] additionalContentParameters);
        Task<bool> GroupExistsAndGMMCanWriteToGroupAsync(SyncJob job);
        Task UpdateSyncJobAsync(SyncStatus? status, SyncJob job);
        Task SendMessageAsync(SyncJob job);
        Task<List<string>> GetGroupEndpointsAsync(SyncJob job);
    }
}
