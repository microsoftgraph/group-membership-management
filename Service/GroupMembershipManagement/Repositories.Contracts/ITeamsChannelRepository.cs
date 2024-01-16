// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface ITeamsChannelRepository
    {
        public Guid RunId { get; set; }
        public Task<List<AzureADTeamsUser>> ReadUsersFromChannelAsync(AzureADTeamsChannel teamsChannel, Guid? runId, string? query = null, bool excludeOwners = true);
        public Task<string> GetChannelTypeAsync(AzureADTeamsChannel teamsChannel, Guid runId);
        public Task<(int SuccessCount, List<AzureADTeamsUser> UsersToRetry, List<AzureADTeamsUser> UsersNotFound)> AddUsersToChannelAsync(AzureADTeamsChannel teamsChannel, ICollection<AzureADTeamsUser> members);
        public Task<(int SuccessCount, List<AzureADTeamsUser> UserRemovesFailed)> RemoveUsersFromChannelAsync(AzureADTeamsChannel teamsChannel, ICollection<AzureADTeamsUser> members);
        public Task<string> GetGroupNameAsync(Guid groupId, Guid runId);
        public Task<List<AzureADUser>> GetGroupOwnersAsync(Guid groupObjectId, Guid runId, int top = 0);
        public Task<Dictionary<string, string>> GetTeamsChannelNamesAsync(List<AzureADTeamsChannel> channels);
        public Task<string> GetTeamsChannelNameAsync(AzureADTeamsChannel channel);
        public Task<bool> IsServiceAccountOwnerOfChannelAsync(Guid serviceAccountObjectId, AzureADTeamsChannel channel, Guid? runId);
        public Task<bool> TeamsChannelExistsAsync(AzureADTeamsChannel channel, Guid? runId);
    }
}