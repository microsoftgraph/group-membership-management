using Entities;
using Microsoft.Graph;
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
            _logger = loggingRepository;
            _graphServiceClient = graphServiceClient;
        }

        public async Task<IEnumerable<AzureADTeamsUser>> ReadUsersFromChannel(Guid groupId, string channelId)
        {
            await _logger.LogMessageAsync(new LogMessage { Message = $"Reading Teams users from group {groupId}, channel {channelId}." });

            var toReturn = new List<AzureADTeamsUser>();

            var members = await _graphServiceClient.Teams[groupId.ToString()].Channels[channelId].Members.Request().GetAsync();

            // x! uses the "null forgiving operator" to fix the nullable/non-nullable type mismatch https://stackoverflow.com/a/54724546
            // it's fine here because the where clause guarantees there's no nulls.
            toReturn.AddRange(members.CurrentPage.Select(ToTeamsUser).Where(x => x != null).Select(x => x!));

            while (members.NextPageRequest != null)
            {
                members = await members.NextPageRequest.GetAsync();
                toReturn.AddRange(members.CurrentPage.Select(ToTeamsUser).Where(x => x != null).Select(x => x!));
            }

            return toReturn;
        }


        private AzureADTeamsUser? ToTeamsUser(ConversationMember member)
        {
            var aadMember = member as AadUserConversationMember;
            if (aadMember == null) { return null; }
            return new AzureADTeamsUser { ObjectId = Guid.Parse(aadMember.UserId), TeamsId = aadMember.Id };
        }
    }
}