// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Models;
using Models.Entities;
using Repositories.Contracts;
using Repositories.GraphGroups;
using System.Collections.Immutable;
using System.Net;

namespace Repositories.TeamsChannel
{
    public class TeamsChannelRepository : ITeamsChannelRepository
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly GraphServiceClient _graphServiceClient;
        private readonly TeamsChannelMetricTracker _teamsChannelMetricTracker;
        public Guid RunId { get; set; }

        public TeamsChannelRepository(ILoggingRepository loggingRepository,
            GraphServiceClient graphServiceClient,
            TelemetryClient telemetryClient)
        {
            _graphServiceClient = graphServiceClient ?? throw new ArgumentNullException(nameof(graphServiceClient));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _teamsChannelMetricTracker = new TeamsChannelMetricTracker(graphServiceClient, telemetryClient, loggingRepository);
        }

        public async Task<List<AzureADTeamsUser>> ReadUsersFromChannelAsync(AzureADTeamsChannel teamsChannel, Guid runId)
        {
            var groupId = teamsChannel.ObjectId;
            var channelId = teamsChannel.ChannelId;

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Reading Teams users from group {groupId}, channel {channelId}." });

            var toReturn = new List<AzureADTeamsUser>();

            try
            {
                var members = await _graphServiceClient.Teams[groupId.ToString()].Channels[channelId].Members.GetAsync();

                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Read {members.Value.Count} Teams users from group {groupId}, channel {channelId}." });

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
                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Read {members.Value.Count} Teams users from group {groupId}, channel {channelId}." });
                }

                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Read a total of {toReturn.Count} Teams users from group {groupId}, channel {channelId}." });

                return toReturn;

            }
            catch (ODataError e)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Exception code:  {e.Error.Code}" });
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Exception Message:  {e.Error.Message}" });
                throw;
            }
            
        }

        public async Task<string> GetChannelTypeAsync(AzureADTeamsChannel teamsChannel, Guid runId)
        {
            try
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Reading metadata about group {teamsChannel.ObjectId}, channel {teamsChannel.ChannelId}." });

                var channelData = await _graphServiceClient.Teams[teamsChannel.ObjectId.ToString()].Channels[teamsChannel.ChannelId].GetAsync();

                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Read metadata about group {teamsChannel.ObjectId}, channel {teamsChannel.ChannelId}. MembershipType is {channelData.MembershipType}." });

                return channelData.MembershipType.ToString();
            }
            catch (ODataError e)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Exception code:  {e.Error.Code}" });
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Exception Message:  {e.Error.Message}" });
                throw;
            }

        }

        private AzureADTeamsUser? ToTeamsUser(ConversationMember member)
        {
            var aadMember = member as AadUserConversationMember;
            if (aadMember?.Roles?.Contains("Owner", StringComparer.InvariantCultureIgnoreCase) ?? false) { return null; }
            return new AzureADTeamsUser { ObjectId = Guid.Parse(aadMember.UserId), ConversationMemberId = aadMember.Id };
        }

        public async Task<(int SuccessCount, List<AzureADTeamsUser> UsersToRetry, List<AzureADTeamsUser> UsersNotFound)> AddUsersToChannelAsync(AzureADTeamsChannel teamsChannel, ICollection<AzureADTeamsUser> members)
        {
            int successCount = 0;
            var usersToRetry = new List<AzureADTeamsUser>();
            var usersNotFound = new List<AzureADTeamsUser>();
            foreach (var member in members)
            {
                var requestBody = CreateRequestBody(member.ObjectId.ToString());
                try
                {
                    var addedMember = await _graphServiceClient.Teams[teamsChannel.ObjectId.ToString()].Channels[teamsChannel.ChannelId.ToString()].Members.PostAsync(requestBody);
                    successCount++;
                }
                catch (ODataError e)
                {
                    if (e.Error.Code == HttpStatusCode.BadRequest.ToString() && e.Error.Message!.Contains("Externally authenticated users and guest users are not allowed in shared channels"))
                    {
                        await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Guest user cannot be added to channel, continuing sync. Exception Message:  {e.Error.Message}" });
                    }

                    if (e.Error.Code == "NotFound" && e.Error.Message!.Contains("Unable to resolve the recipient."))
                    {
                        await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"User not found with Object Id: {member.ObjectId}, sync will fail. Exception Message:  {e.Error.Message}" });
                        usersNotFound.Add(member);

                        continue;
                    }

                    if (e.Error.Code == "UnknownError" || e.Error.Code == HttpStatusCode.BadGateway.ToString())
                    {
                        await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"An unknown error occurred for user {member.ObjectId}, but continuing sync. Exception Message:  {e.Error.Message}" });
                        usersToRetry.Add(member);

                        continue;
                    }

                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Exception code:  {e.Error.Code}" });
                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Exception Message:  {e.Error.Message}" });

                    throw;
                }
            }
            return (successCount, usersToRetry, usersNotFound);
        }
        public async Task<(int SuccessCount, List<AzureADTeamsUser> UserRemovesFailed)> RemoveUsersFromChannelAsync(AzureADTeamsChannel teamsChannel, ICollection<AzureADTeamsUser> members)
        {
            int successCount = 0;
            var usersNotFound = new List<AzureADTeamsUser>();
            foreach (var member in members)
            {
                try
                {
                    var conversationMember = member as AzureADTeamsUser;
                    await _graphServiceClient.Teams[teamsChannel.ObjectId.ToString()]
                        .Channels[teamsChannel.ChannelId.ToString()]
                        .Members[conversationMember.ConversationMemberId]
                        .DeleteAsync();
                    successCount++;
                }
                catch (ODataError e)
                {
                    if(e.Error.Code == HttpStatusCode.BadRequest.ToString() && e.Error.Message!.Contains("Invalid id"))
                    {
                        usersNotFound.Add(member);
                        await _loggingRepository.LogMessageAsync(new LogMessage {
                            Message = $"An invalid id was found for user with object id '{member.ObjectId}' and conversation id '{member.ConversationMemberId}', but continuing sync. Exception Message: {e.Error.Message}" });
                        continue;
                    }

                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Exception code:  {e.Error.Code}" });
                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Exception Message:  {e.Error.Message}" });
                    throw;
                }
            }
            return (successCount, usersNotFound); 
        }
        private ConversationMember CreateRequestBody(string userObjectId)
        {
            var requestBody = new ConversationMember
            {
                OdataType = "#microsoft.graph.aadUserConversationMember",
                AdditionalData = new Dictionary<string, object>
                {
                    {
                        "user@odata.bind" , $"https://graph.microsoft.com/v1.0/users('{userObjectId}')"
                    },
                }
            };
            return requestBody;
        }

        public async Task<string> GetGroupNameAsync(Guid groupId, Guid runId)
        {
            try
            {
                var nativeResponseHandler = new NativeResponseHandler();
                var responseHandlerOption = new ResponseHandlerOption { ResponseHandler = nativeResponseHandler };
                Group group = null;

                await _graphServiceClient.Groups[groupId.ToString()].GetAsync(requestConfiguration =>
                {
                    requestConfiguration.Options.Add(responseHandlerOption);
                });

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;
                if (nativeResponse.IsSuccessStatusCode)
                {
                    group = await DeserializeResponseAsync(nativeResponse, Group.CreateFromDiscriminatorValue);
                }

                var headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                await _teamsChannelMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);

                return group != null ? group.DisplayName : string.Empty;
            }
            catch (ODataError ex)
            {
                if (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
                    return string.Empty;

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = runId
                });

                throw;
            }
        }

        public async Task<List<AzureADUser>> GetGroupOwnersAsync(Guid groupObjectId, Guid runId, int top = 0)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                RunId = runId,
                Message = $"Getting owners of group {groupObjectId}."
            });

            var owners = new List<User>();

            try
            {
                var nativeResponseHandler = new NativeResponseHandler();
                var groupOwnersResponse = new DirectoryObjectCollectionResponse();

                await _graphServiceClient.Groups[groupObjectId.ToString()].Owners.GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Top = top > 0 ? top : null;
                    requestConfiguration.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                });

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

                if (nativeResponse.IsSuccessStatusCode)
                {
                    groupOwnersResponse = await DeserializeResponseAsync(nativeResponse,
                                                                         DirectoryObjectCollectionResponse.CreateFromDiscriminatorValue);

                    var headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);

                    owners.AddRange(groupOwnersResponse.Value.OfType<User>());
                    await _teamsChannelMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);

                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        RunId = runId,
                        Message = $"Retrieved{(top > 0 ? " top " : " ")}{owners.Count} owners of group {groupObjectId}."
                    });

                    return owners.Select(x => new AzureADUser
                    {
                        ObjectId = Guid.Parse(x.Id),
                        Mail = x.Mail
                    })
                    .ToList();
                }

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = runId,
                    Message = $"Failed to retrieve owners of group {groupObjectId}. StatusCode {nativeResponse.StatusCode}"
                });

                return new List<AzureADUser>();

            }
            catch (ODataError ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = runId
                });

                throw;
            }
        }

        protected async Task<T> DeserializeResponseAsync<T>(HttpResponseMessage response, ParsableFactory<T> factory) where T : IParsable
        {
            var rootNode = await GetRootParseNodeAsync(response);
            if (rootNode == null) return default;
            var result = rootNode.GetObjectValue(factory);
            return result;
        }

        private async Task<IParseNode> GetRootParseNodeAsync(HttpResponseMessage response)
        {
            var pNodeFactory = ParseNodeFactoryRegistry.DefaultInstance;
            var responseContentType = response.Content?.Headers?.ContentType?.MediaType?.ToLowerInvariant();

            if (string.IsNullOrEmpty(responseContentType))
                return null;

            using var contentStream = await (response.Content?.ReadAsStreamAsync() ?? Task.FromResult(Stream.Null));
            var rootNode = pNodeFactory.GetRootParseNode(responseContentType!, contentStream);
            return rootNode;
        }
    }
}
