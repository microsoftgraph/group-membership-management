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
        Task<List<SyncJob>> GetSyncJobsAsync();
        Task<SyncJob> GetSyncJobByIdAsync(Guid syncJobId);
        Task<Group> GetGroupAsync(SyncJob syncJob);
        Task<Channel> GetChannelAsync(SyncJob syncJob);
        Task<ParsedAndValidateDestinationResponse> ParseAndValidateDestinationAsync(SyncJob syncJob);
        Task<string> GetDestinationNameAsync(SyncJob job);
        Task SendEmailAsync(SyncJob job, NotificationMessageType notificationType, string[] additionalContentParameters);
        Task<DestinationVerifierResult> DestinationExistsAndGMMCanWriteToItAsync(SyncJob job);
        Task UpdateSyncJobAsync(SyncStatus? status, SyncJob job);
        Task<bool> TryClaimAndUpdateJobAsync(SyncStatus status, SyncJob job);
        Task SendMessageAsync(SyncJob job);
        Task<List<string>> GetGroupEndpointsAsync(SyncJob job);
        Task UpdateSyncJobDestinationAsync(Guid jobId, string destination);
    }
}
