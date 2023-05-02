// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Models;
using Models.Entities;
using Repositories.Contracts;

namespace Repositories.TeamsChannel
{
    public class TeamsChannelRepository : ITeamsChannelRepository
    {
        private readonly ILoggingRepository _logger;
        private readonly GraphServiceClient _graphServiceClient;

        public TeamsChannelRepository(ILoggingRepository loggingRepository, GraphServiceClient graphServiceClient)
        {
            _graphServiceClient = graphServiceClient ?? throw new ArgumentNullException(nameof(graphServiceClient));
            _logger = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        public async Task<List<AzureADTeamsUser>> ReadUsersFromChannelAsync(AzureADTeamsChannel teamsChannel, Guid runId)
        {
            var groupId = teamsChannel.ObjectId;
            var channelId = teamsChannel.ChannelId;

            await _logger.LogMessageAsync(new LogMessage { Message = $"Reading Teams users from group {groupId}, channel {channelId}." });

            var toReturn = new List<AzureADTeamsUser>();

            var members = await _graphServiceClient.Teams[groupId.ToString()].Channels[channelId].Members.GetAsync();

            await _logger.LogMessageAsync(new LogMessage { Message = $"Read {members.Value.Count} Teams users from group {groupId}, channel {channelId}." });

            // x! uses the "null forgiving operator" to fix the nullable/non-nullable type mismatch https://stackoverflow.com/a/54724546
            // it's fine here because the where clause guarantees there's no nulls.
            toReturn.AddRange(members.Value.Select(ToTeamsUser).Where(x => x != null).Select(x => x!));

            while (members.OdataNextLink != null)
            {
                var request = new RequestInformation
                {
                    HttpMethod = Method.GET,
                    UrlTemplate = members.OdataNextLink
                };

                members = await _graphServiceClient.RequestAdapter.SendAsync<ConversationMemberCollectionResponse>(request, ConversationMemberCollectionResponse.CreateFromDiscriminatorValue);
                toReturn.AddRange(members.Value.Select(ToTeamsUser).Where(x => x != null).Select(x => x!));
                await _logger.LogMessageAsync(new LogMessage { Message = $"Read {members.Value.Count} Teams users from group {groupId}, channel {channelId}." });
            }

            await _logger.LogMessageAsync(new LogMessage { Message = $"Read a total of {toReturn.Count} Teams users from group {groupId}, channel {channelId}." });

            return toReturn;
        }

        public async Task<string> GetChannelTypeAsync(AzureADTeamsChannel teamsChannel, Guid runId)
        {
            await _logger.LogMessageAsync(new LogMessage { Message = $"Reading metadata about group {teamsChannel.ObjectId}, channel {teamsChannel.ChannelId}." });

            var channelData = await _graphServiceClient.Teams[teamsChannel.ObjectId.ToString()].Channels[teamsChannel.ChannelId].GetAsync();

            await _logger.LogMessageAsync(new LogMessage { Message = $"Read metadata about group {teamsChannel.ObjectId}, channel {teamsChannel.ChannelId}. MembershipType is {channelData.MembershipType}." });

            return channelData.MembershipType.ToString();
        }


        private AzureADTeamsUser? ToTeamsUser(ConversationMember member)
        {
            var aadMember = member as AadUserConversationMember;
            if (aadMember == null) { return null; }
            return new AzureADTeamsUser { ObjectId = Guid.Parse(aadMember.UserId), TeamsId = aadMember.Id };
        }
    }
}
