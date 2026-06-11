// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Entities;
using Repositories.Contracts;
using Channel = Microsoft.Graph.Models.Channel;

namespace Repositories.TeamsChannel
{
    /// <summary>
    /// A no-op implementation of ITeamsChannelRepository used when Teams Channel functionality is disabled.
    /// All methods throw InvalidOperationException to indicate Teams Channel is not configured.
    /// </summary>
    public class DisabledTeamsChannelRepository : ITeamsChannelRepository
    {
        public Guid RunId { get; set; }

        private static InvalidOperationException TeamsChannelDisabledException() =>
            new("Teams Channel functionality is disabled. Set 'TeamsChannel:EnableTeamsChannel' to true and provide valid credentials.");

        public Task<List<AzureADTeamsUser>> ReadUsersFromChannelAsync(AzureADTeamsChannel teamsChannel, Guid? runId, string? query = null, bool excludeOwners = true)
            => throw TeamsChannelDisabledException();

        public Task<string> GetChannelTypeAsync(AzureADTeamsChannel teamsChannel, Guid runId)
            => throw TeamsChannelDisabledException();

        public Task<(int SuccessCount, List<AzureADTeamsUser> UsersToRetry, List<AzureADTeamsUser> UsersNotFound)> AddUsersToChannelAsync(AzureADTeamsChannel teamsChannel, ICollection<AzureADTeamsUser> members)
            => throw TeamsChannelDisabledException();

        public Task<(int SuccessCount, List<AzureADTeamsUser> UserRemovesFailed)> RemoveUsersFromChannelAsync(AzureADTeamsChannel teamsChannel, ICollection<AzureADTeamsUser> members)
            => throw TeamsChannelDisabledException();

        public Task<string> GetGroupNameAsync(Guid groupId, Guid runId)
            => throw TeamsChannelDisabledException();

        public Task<Channel> GetMainChannelAsync(Guid teamObjectId)
            => throw TeamsChannelDisabledException();

        public Task<List<Channel>> SearchTeamsChannelsAsync(Guid teamObjectId, string filter)
            => throw TeamsChannelDisabledException();

        public Task<Dictionary<string, string>> GetTeamsChannelEmailsAsync(List<AzureADTeamsChannel> channels)
            => throw TeamsChannelDisabledException();

        public Task<List<AzureADUser>> GetGroupOwnersAsync(Guid groupObjectId, Guid runId, int top = 0)
            => throw TeamsChannelDisabledException();

        public Task<Dictionary<string, string>> GetTeamsChannelNamesAsync(List<AzureADTeamsChannel> channels)
            => throw TeamsChannelDisabledException();

        public Task<string> GetTeamsChannelNameAsync(AzureADTeamsChannel channel)
            => throw TeamsChannelDisabledException();

        public Task<bool> IsServiceAccountOwnerOfChannelAsync(Guid serviceAccountObjectId, AzureADTeamsChannel channel, Guid? runId)
            => throw TeamsChannelDisabledException();

        public Task<bool> TeamsChannelExistsAsync(AzureADTeamsChannel channel, Guid? runId)
            => throw TeamsChannelDisabledException();
    }
}
