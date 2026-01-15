// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;
using Models;
using Models.Entities;
using Repositories.Contracts;
using Repositories.GraphGroups;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Linq;
using Channel = Microsoft.Graph.Models.Channel;
using Group = Microsoft.Graph.Models.Group;

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

        public async Task<List<AzureADTeamsUser>> ReadUsersFromChannelAsync(AzureADTeamsChannel teamsChannel, Guid? runId, string? query = null, bool excludeOwners = true)
        {
            var groupId = teamsChannel.ObjectId;
            var channelId = teamsChannel.ChannelId;

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Reading Teams users from group {groupId}, channel {channelId}." });

            var toReturn = new List<AzureADTeamsUser>();

            try
            {
                var nativeResponseHandler = new NativeResponseHandler();

                await _graphServiceClient.Teams[groupId.ToString()].Channels[channelId].Members.GetAsync(requestConfiguration =>
                {
                    if (!string.IsNullOrEmpty(query))
                    {
                        requestConfiguration.QueryParameters.Filter = query;
                    }
                    requestConfiguration.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                });

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

                if (nativeResponse == null)
                {
                    return toReturn;
                }

                var headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                await _teamsChannelMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);

                if (!nativeResponse.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Failed to read Teams channel members. Status code: {nativeResponse.StatusCode}");
                }

                var membersPage = await DeserializeResponseAsync(nativeResponse, ConversationMemberCollectionResponse.CreateFromDiscriminatorValue);

                if (membersPage?.Value != null)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Read {membersPage.Value.Count} Teams users from group {groupId}, channel {channelId}." });
                    // x! uses the "null forgiving operator" to fix the nullable/non-nullable type mismatch https://stackoverflow.com/a/54724546
                    // it's fine here because the where clause guarantees there's no nulls.
                    toReturn.AddRange(membersPage.Value.Select((member) => ToTeamsUser(member, excludeOwners)).Where(x => x != null).Select(x => x!));
                }

                var nextLink = membersPage?.OdataNextLink;

                while (!string.IsNullOrEmpty(nextLink))
                {
                    var request = new RequestInformation
                    {
                        HttpMethod = Method.GET,
                        UrlTemplate = nextLink
                    };

                    var pageResponseHandler = new NativeResponseHandler();
                    request.AddRequestOptions(new List<IRequestOption> { new ResponseHandlerOption { ResponseHandler = pageResponseHandler } });

                    await _graphServiceClient.RequestAdapter.SendAsync<ConversationMemberCollectionResponse>(request, ConversationMemberCollectionResponse.CreateFromDiscriminatorValue);

                    var pageResponse = pageResponseHandler.Value as HttpResponseMessage;

                    if (pageResponse == null)
                    {
                        break;
                    }

                    var pageHeaders = pageResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                    await _teamsChannelMetricTracker.TrackMetricsAsync(pageHeaders, QueryType.Other, runId);

                    if (!pageResponse.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"Failed to read Teams channel members page. Status code: {pageResponse.StatusCode}");
                    }

                    membersPage = await DeserializeResponseAsync(pageResponse, ConversationMemberCollectionResponse.CreateFromDiscriminatorValue);

                    if (membersPage?.Value != null)
                    {
                        toReturn.AddRange(membersPage.Value.Select((member) => ToTeamsUser(member, excludeOwners)).Where(x => x != null).Select(x => x!));
                        await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Read {membersPage.Value.Count} Teams users from group {groupId}, channel {channelId}." });
                    }

                    nextLink = membersPage?.OdataNextLink;
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

                var nativeResponseHandler = new NativeResponseHandler();

                await _graphServiceClient.Teams[teamsChannel.ObjectId.ToString()].Channels[teamsChannel.ChannelId].GetAsync(requestConfiguration =>
                {
                    requestConfiguration.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                });

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

                if (nativeResponse == null)
                {
                    return string.Empty;
                }

                try
                {
                    await TrackResponseMetricsAsync(nativeResponse, ResolveRunId(runId));

                    if (!nativeResponse.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"Failed to read metadata about group {teamsChannel.ObjectId}, channel {teamsChannel.ChannelId}. Status code: {nativeResponse.StatusCode}");
                    }

                    var channelData = await DeserializeResponseAsync(nativeResponse, Channel.CreateFromDiscriminatorValue);

                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Read metadata about group {teamsChannel.ObjectId}, channel {teamsChannel.ChannelId}. MembershipType is {channelData?.MembershipType}." });

                    return channelData?.MembershipType?.ToString() ?? string.Empty;
                }
                finally
                {
                    nativeResponse.Dispose();
                }
            }
            catch (ODataError e)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Exception code:  {e.Error.Code}" });
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Exception Message:  {e.Error.Message}" });
                throw;
            }

        }

        private AzureADTeamsUser? ToTeamsUser(ConversationMember member, bool excludeOwners)
        {
            var aadMember = member as AadUserConversationMember;
            if (excludeOwners && (aadMember?.Roles?.Contains("Owner", StringComparer.InvariantCultureIgnoreCase) ?? false)) { return null; }
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
                    var nativeResponseHandler = new NativeResponseHandler();

                    await _graphServiceClient.Teams[teamsChannel.ObjectId.ToString()].Channels[teamsChannel.ChannelId.ToString()].Members.PostAsync(requestBody, requestConfiguration =>
                    {
                        requestConfiguration.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                    });

                    var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

                    if (nativeResponse != null)
                    {
                        try
                        {
                            await TrackResponseMetricsAsync(nativeResponse, ResolveRunId());
                        }
                        finally
                        {
                            nativeResponse.Dispose();
                        }
                    }

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
                    var nativeResponseHandler = new NativeResponseHandler();

                    await _graphServiceClient.Teams[teamsChannel.ObjectId.ToString()]
                        .Channels[teamsChannel.ChannelId.ToString()]
                        .Members[conversationMember.ConversationMemberId]
                        .DeleteAsync(requestConfiguration =>
                        {
                            requestConfiguration.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                        });

                    var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

                    if (nativeResponse != null)
                    {
                        try
                        {
                            await TrackResponseMetricsAsync(nativeResponse, ResolveRunId());
                        }
                        finally
                        {
                            nativeResponse.Dispose();
                        }
                    }

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
        public async Task<Dictionary<string, string>> GetTeamsChannelNamesAsync(List<AzureADTeamsChannel> channels)
        {
            var channelNames = new Dictionary<string, string>();
            var batchRequest = new BatchRequestContentCollection(_graphServiceClient);

            // requestId, groupId
            var requestIdTracker = new Dictionary<string, string>();

            foreach (var channel in channels.Distinct())
            {
                var requestInformation = _graphServiceClient
                                            .Teams[channel.ObjectId.ToString()]
                                            .Channels[channel.ChannelId.ToString()]
                                            .ToGetRequestInformation(requestConfiguration =>
                                            {
                                                requestConfiguration.QueryParameters.Select = new[] { "displayName" };
                                            });



                var requestId = await batchRequest.AddBatchRequestStepAsync(requestInformation);
                requestIdTracker.Add(requestId, channel.ChannelId);
            }

            var batchResponse = await _graphServiceClient.Batch.PostAsync(batchRequest);

            foreach (var statusCodeResponse in await batchResponse.GetResponsesStatusCodesAsync())
            {
                using var response = await batchResponse.GetResponseByIdAsync(statusCodeResponse.Key);

                await TrackResponseMetricsAsync(response, ResolveRunId());

                if (response.IsSuccessStatusCode)
                {
                    var responseHandler = new ResponseHandler<Channel>();
                    var channel = await responseHandler.HandleResponseAsync<HttpResponseMessage, Channel>(response, null);
                    if (channel != null)
                        channelNames.Add(requestIdTracker[statusCodeResponse.Key], channel.DisplayName);
                }
                else
                {
                    channelNames.Add(requestIdTracker[statusCodeResponse.Key], null);
                }
            }

            return channelNames;
        }
        public async Task<List<Channel>> SearchTeamsChannelsAsync(Guid teamObjectId, string filter)
        {
            try
            {
                var nativeResponseHandler = new NativeResponseHandler();

                await _graphServiceClient.Teams[teamObjectId.ToString()]
                    .Channels
                    .GetAsync(requestConfig =>
                    {
                        requestConfig.QueryParameters.Select = ["id", "displayName", "membershipType"];
                        requestConfig.QueryParameters.Filter = filter;
                        requestConfig.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                    });

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

                if (nativeResponse == null)
                {
                    return new List<Channel>();
                }

                try
                {
                    await TrackResponseMetricsAsync(nativeResponse, ResolveRunId());

                    if (!nativeResponse.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"Failed to search Teams channels for team {teamObjectId}. Status code: {nativeResponse.StatusCode}");
                    }

                    var channelResponse = await DeserializeResponseAsync(nativeResponse, ChannelCollectionResponse.CreateFromDiscriminatorValue);

                    return channelResponse?.Value?.ToList() ?? new List<Channel>();
                }
                finally
                {
                    nativeResponse.Dispose();
                }
            }
            catch (ODataError ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString()
                });

                throw;
            }
        }
        public async Task<Channel> GetMainChannelAsync(Guid teamObjectId)
        {
            var teamResponseHandler = new NativeResponseHandler();

            await _graphServiceClient.Teams[teamObjectId.ToString()]
                .GetAsync(requestConfig =>
                {
                    requestConfig.QueryParameters.Select = new[] { "internalId" };
                    requestConfig.Options.Add(new ResponseHandlerOption { ResponseHandler = teamResponseHandler });
                });

            var teamResponse = teamResponseHandler.Value as HttpResponseMessage;

            if (teamResponse == null)
            {
                return null;
            }

            Team team = null;

            try
            {
                await TrackResponseMetricsAsync(teamResponse, ResolveRunId());

                if (!teamResponse.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Failed to read team {teamObjectId}. Status code: {teamResponse.StatusCode}");
                }

                team = await DeserializeResponseAsync(teamResponse, Team.CreateFromDiscriminatorValue);
            }
            finally
            {
                teamResponse.Dispose();
            }

            if (team == null || string.IsNullOrEmpty(team.InternalId))
            {
                return null;
            }

            var channelResponseHandler = new NativeResponseHandler();

            await _graphServiceClient.Teams[teamObjectId.ToString()]
                .Channels[team.InternalId]
                .GetAsync(requestConfig =>
                {
                    requestConfig.QueryParameters.Select = new[] { "id", "displayName", "email" };
                    requestConfig.Options.Add(new ResponseHandlerOption { ResponseHandler = channelResponseHandler });
                });

            var channelResponse = channelResponseHandler.Value as HttpResponseMessage;

            if (channelResponse == null)
            {
                return null;
            }

            try
            {
                await TrackResponseMetricsAsync(channelResponse, ResolveRunId());

                if (!channelResponse.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Failed to read channel {team.InternalId} for team {teamObjectId}. Status code: {channelResponse.StatusCode}");
                }

                return await DeserializeResponseAsync(channelResponse, Channel.CreateFromDiscriminatorValue);
            }
            finally
            {
                channelResponse.Dispose();
            }
        }
        public async Task<Dictionary<string,string>> GetTeamsChannelEmailsAsync(List<AzureADTeamsChannel> channels)
        {
            var channelEmails = new Dictionary<string, string>();
            var batchRequest = new BatchRequestContentCollection(_graphServiceClient);
            var requestIdTracker = new Dictionary<string, string>();

            foreach (var channel in channels.Distinct())
            {
                var requestInformation = _graphServiceClient
                    .Teams[channel.ObjectId.ToString()]
                    .Channels[channel.ChannelId]
                    .ToGetRequestInformation(requestConfig =>
                    {
                        requestConfig.QueryParameters.Select = new[] { "email" };
                    });

                var requestId = await batchRequest.AddBatchRequestStepAsync(requestInformation);
                requestIdTracker.Add(requestId, channel.ChannelId);
            }

            var batchResponse = await _graphServiceClient.Batch.PostAsync(batchRequest);

            foreach (var statusCodeResponse in await batchResponse.GetResponsesStatusCodesAsync())
            {
                using var response = await batchResponse.GetResponseByIdAsync(statusCodeResponse.Key);

                await TrackResponseMetricsAsync(response, ResolveRunId());

                if (response.IsSuccessStatusCode)
                {
                    var responseHandler = new ResponseHandler<Channel>();
                    var channelObj = await responseHandler.HandleResponseAsync<HttpResponseMessage, Channel>(response, null);

                    channelEmails[requestIdTracker[statusCodeResponse.Key]] = channelObj?.Email; 
                }
                else
                {
                    channelEmails[requestIdTracker[statusCodeResponse.Key]] = null;
                }
            }

            return channelEmails;
        }

        public async Task<string> GetTeamsChannelNameAsync(AzureADTeamsChannel channel)
        {
            var name = (await GetTeamsChannelNamesAsync(new List<AzureADTeamsChannel>() { channel }))[channel.ChannelId];
            return name;
        }
        public async Task<bool> IsServiceAccountOwnerOfChannelAsync(Guid serviceAccountObjectId, AzureADTeamsChannel channel, Guid? runId)
        {
            var userList = await ReadUsersFromChannelAsync(channel, runId, $"roles/any(r: tolower(r) eq 'owner')", false);
            return userList.Any(user => user.ObjectId == serviceAccountObjectId);
        }

        public async Task<bool> TeamsChannelExistsAsync(AzureADTeamsChannel channel, Guid? runId)
        {
            try
            {
                var nativeResponseHandler = new NativeResponseHandler();
                var responseHandlerOption = new ResponseHandlerOption { ResponseHandler = nativeResponseHandler };
                var retryHandlerOption = new RetryHandlerOption { MaxRetry = 4, Delay = 20 };

                bool? channelExists = null;

                await _graphServiceClient.Teams[channel.ObjectId.ToString()].Channels[channel.ChannelId].GetAsync(requestConfiguration =>
                {
                    requestConfiguration.Options.Add(retryHandlerOption);
                    requestConfiguration.Options.Add(responseHandlerOption);
                });

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;
                var statusCode = nativeResponse?.StatusCode;
                if (nativeResponse != null)
                {
                    try
                    {
                        await TrackResponseMetricsAsync(nativeResponse, ResolveRunId(runId));

                        if (nativeResponse.IsSuccessStatusCode)
                        {
                            var response = await DeserializeResponseAsync(nativeResponse, Channel.CreateFromDiscriminatorValue);
                            channelExists = response?.Id != null;
                        }
                        else if (nativeResponse.StatusCode == HttpStatusCode.NotFound)
                        {
                            channelExists = false;
                        }
                    }
                    finally
                    {
                        nativeResponse.Dispose();
                    }
                }
                else
                {
                    channelExists = null;
                }

                if (!channelExists.HasValue)
                {
                    throw new Exception($"Unable to determine if channel {{ objectId: {channel.ObjectId} channelId: {channel.ChannelId} }} exists. Status code: {statusCode?.ToString() ?? "(none)"}");
                }

                return channelExists.Value;
            }
            catch (ODataError ex)
            {
                if (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
                    return false;

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = runId
                });

                throw;
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = runId
                });

                throw;
            }
        }

        private Guid? ResolveRunId(Guid? runId = null)
        {
            if (runId.HasValue && runId.Value != Guid.Empty)
            {
                return runId.Value;
            }

            return RunId == Guid.Empty ? null : RunId;
        }

        private async Task TrackResponseMetricsAsync(HttpResponseMessage response, Guid? runId)
        {
            if (response == null)
            {
                return;
            }

            var headers = GetResponseHeaders(response);

            if (headers != null)
            {
                await _teamsChannelMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);
            }
        }

        private static IDictionary<string, IEnumerable<string>> GetResponseHeaders(HttpResponseMessage response)
        {
            if (response == null)
            {
                return null;
            }

            var headers = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var header in response.Headers)
            {
                headers[header.Key] = header.Value;
            }

            if (response.Content != null)
            {
                foreach (var header in response.Content.Headers)
                {
                    headers[header.Key] = header.Value;
                }
            }

            return headers;
        }
    }
}
