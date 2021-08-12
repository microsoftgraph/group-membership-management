// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Graph;
using Polly;
using Repositories.Contracts;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
    public class MockGraphUpdaterService : IGraphUpdaterService
    {
        IMailRepository _mailRepository;

        public Dictionary<Guid, Group> Groups { get; set; } = new Dictionary<Guid, Group>();
        public Dictionary<Guid, List<AzureADUser>> GroupsToUsers { get; set; } = new Dictionary<Guid, List<AzureADUser>>();
        public Dictionary<(string, string), SyncJob> Jobs { get; set; } = new Dictionary<(string, string), SyncJob>();

        public MockGraphUpdaterService(IMailRepository mailRepository)
        {
            _mailRepository = mailRepository ?? throw new ArgumentNullException(nameof(mailRepository));
        }

        public Task<Services.Entities.UsersPageResponse> GetFirstMembersPageAsync(Guid groupId, Guid runId)
        {
            throw new NotImplementedException();
        }

        public async Task<string> GetGroupNameAsync(Guid groupId)
        {
            return await Task.FromResult(Groups[groupId].DisplayName);
        }

        public Task<Services.Entities.UsersPageResponse> GetNextMembersPageAsync(string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage membersPage)
        {
            throw new NotImplementedException();
        }

        public async Task<SyncJob> GetSyncJobAsync(string partitionKey, string rowKey)
        {
            var result = Jobs.ContainsKey((partitionKey, rowKey)) ? Jobs[(partitionKey, rowKey)] : null;
            return await Task.FromResult(result);
        }

        public async Task<PolicyResult<bool>> GroupExistsAsync(Guid groupId, Guid runId)
        {
            var groupExists = Groups.ContainsKey(groupId);
            var result = PolicyResult<bool>.Successful(groupExists, new Context());
            return await Task.FromResult(result);
        }

        public async Task SendEmailAsync(string toEmail, string contentTemplate, string[] additionalContentParams, Guid runId, string ccEmail = null)
        {
            var message = new EmailMessage
            {
                AdditionalContentParams = additionalContentParams,
                CcEmailAddresses = ccEmail,
                Content = contentTemplate,
                ToEmailAddresses = toEmail
            };

            await _mailRepository.SendMailAsync(message, runId);
        }

        public Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status, bool isDryRun, Guid runId)
        {
            throw new NotImplementedException();
        }

        public Task<GraphUpdaterStatus> AddUsersToGroupAsync(ICollection<AzureADUser> members, Guid targetGroupId, Guid runId)
        {
            throw new NotImplementedException();
        }

        public Task<GraphUpdaterStatus> RemoveUsersFromGroupAsync(ICollection<AzureADUser> members, Guid targetGroupId, Guid runId)
        {
            throw new NotImplementedException();
        }
    }
}
