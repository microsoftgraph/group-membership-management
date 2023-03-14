// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Entities;
using Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IJobTriggerService
    {
        public Guid RunId { get; set; }
        Task<TableSegmentBulkResult<SyncJob>> GetSyncJobsSegmentAsync(AsyncPageable<SyncJob> pageableQueryResult, string continuationToken);
        Task<string> GetGroupNameAsync(Guid groupId);
        Task SendEmailAsync(SyncJob job, string emailSubjectTemplateName, string emailContentTemplateName, string[] additionalContentParameters, string templateDirectory = "");
        Task<bool> GroupExistsAndGMMCanWriteToGroupAsync(SyncJob job, string templateDirectory = "");
        Task UpdateSyncJobStatusAsync(SyncStatus status, SyncJob job);
        Task SendMessageAsync(SyncJob job);
        Task<List<string>> GetGroupEndpointsAsync(Guid groupId);
    }
}
