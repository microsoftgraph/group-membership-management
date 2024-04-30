// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.Notifications;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IJobTriggerService
    {
        public Guid RunId { get; set; }
        Task<(List<SyncJob> jobs, bool jobTriggerThresholdExceeded, int maxJobsAllowed)> GetSyncJobsAsync();
        Task<(bool IsValid, string DestinationObject)> ParseAndValidateDestinationAsync(SyncJob syncJob);
        Task<string> GetDestinationNameAsync(SyncJob job);
        Task SendEmailAsync(SyncJob job, NotificationMessageType notificationType, string[] additionalContentParameters);
        Task<DestinationVerifierResult> DestinationExistsAndGMMCanWriteToItAsync(SyncJob job);
        Task UpdateSyncJobAsync(SyncStatus? status, SyncJob job);
        Task SendMessageAsync(SyncJob job);
        Task<List<string>> GetGroupEndpointsAsync(SyncJob job);
    }
}
