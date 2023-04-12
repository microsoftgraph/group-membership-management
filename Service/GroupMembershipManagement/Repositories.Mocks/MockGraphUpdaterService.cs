// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Graph;
using Models;
using Models.Entities;
using Polly;
using Repositories.Contracts;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
    public class MockGraphUpdaterService : IGraphUpdaterService
    {
        IMailRepository _mailRepository;

        public Dictionary<Guid, Group> Groups { get; set; } = new Dictionary<Guid, Group>();
        public Dictionary<Guid, List<AzureADUser>> GroupsToUsers { get; set; } = new Dictionary<Guid, List<AzureADUser>>();
        public Dictionary<(string, string), SyncJob> Jobs { get; set; } = new Dictionary<(string, string), SyncJob>();
        public Guid RunId { get; set; }

        public MockGraphUpdaterService(IMailRepository mailRepository)
        {
            _mailRepository = mailRepository ?? throw new ArgumentNullException(nameof(mailRepository));
        }

        public Task<UsersPageResponse> GetFirstMembersPageAsync(Guid groupId, Guid runId)
        {
            throw new NotImplementedException();
        }

        public async Task<string> GetGroupNameAsync(Guid groupId)
        {
            return await Task.FromResult(Groups[groupId].DisplayName);
        }

        public Task<UsersPageResponse> GetNextMembersPageAsync(string nextPageUrl, Guid runId)
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

        public async Task SendEmailAsync(string toEmail, string contentTemplate, string[] additionalContentParams, Guid runId, string ccEmail = null, string emailSubject = null, string[] additionalSubjectParams = null, string adaptiveCardTemplateDirectory = "")
        {
            var message = new EmailMessage
            {
                AdditionalContentParams = additionalContentParams,
                CcEmailAddresses = ccEmail,
                Content = contentTemplate,
                ToEmailAddresses = toEmail,
                AdditionalSubjectParams = additionalSubjectParams
            };

            await _mailRepository.SendMailAsync(message, runId);
        }

        public Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status, bool isDryRun, Guid runId)
        {
            job.RunId = runId;
            job.Status = status.ToString();
            var isDryRunSync = job.IsDryRunEnabled || isDryRun;

            if (isDryRunSync)
                job.DryRunTimeStamp = DateTime.UtcNow;
            else
                job.LastRunTime = DateTime.UtcNow;

            Jobs[(job.PartitionKey, job.RowKey)] = job;

            return Task.CompletedTask;
        }

        public Task<(GraphUpdaterStatus Status, int SuccessCount, List<AzureADUser> UsersNotFound)> AddUsersToGroupAsync(ICollection<AzureADUser> members, Guid targetGroupId, Guid runId, bool isinitialSync)
        {
            throw new NotImplementedException();
        }

        public Task<(GraphUpdaterStatus Status, int SuccessCount, List<AzureADUser> UsersNotFound)> RemoveUsersFromGroupAsync(ICollection<AzureADUser> members, Guid targetGroupId, Guid runId, bool isinitialSync)
        {
            throw new NotImplementedException();
        }

        public Task<bool> IsEmailRecipientOwnerOfGroupAsync(string email, Guid groupObjectId)
        {
            var owners = Groups[groupObjectId].Owners;
            var isOwner = owners != null && owners.OfType<User>().Where(x => x.Mail.Equals(email, StringComparison.InvariantCultureIgnoreCase)).Any();
            return Task.FromResult(isOwner);
        }

        public Task<List<User>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0)
        {
            var allOwners = Groups[groupObjectId].Owners;
            var userOwners = allOwners == null ? new List<User>() : allOwners.OfType<User>().ToList();
            return Task.FromResult(userOwners);
        }

        public Task SendEmailAsync(string toEmail, string contentTemplate, string[] additionalContentParams, Guid runId, string ccEmail = null, string emailSubject = null, string[] additionalSubjectParams = null)
        {
            throw new NotImplementedException();
        }
    }
}
