// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Notifications;
using Polly;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IGraphAPIService
    {
        public Guid RunId { get; set; }
        public Task<string> GetGroupNameAsync(Guid groupId);
        public Task<PolicyResult<bool>> GroupExistsAsync(Guid groupId, Guid runId);
        public Task<List<AzureADUser>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0);
        public Task<bool> IsEmailRecipientOwnerOfGroupAsync(string email, Guid groupObjectId);
        public Task SendEmailAsync(SyncJob job, NotificationMessageType notificationType, string[] additionalContentParameters);
    }
}