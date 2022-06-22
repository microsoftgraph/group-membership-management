// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.ApplicationInsights;
using Microsoft.Graph;
using Newtonsoft.Json.Linq;
using Polly;
using Polly.Retry;
using Repositories.Contracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Group = Microsoft.Graph.Group;
using Metric = Services.Entities.Metric;

namespace Repositories.GraphGroups
{
    public class GraphGroupRepository : IGraphGroupRepository
    {
        private readonly IGraphServiceClient _graphServiceClient;
        private readonly TelemetryClient _telemetryClient;
        private readonly ILoggingRepository _loggingRepository;

        public Guid RunId { get; set; }

        public GraphGroupRepository(IGraphServiceClient graphServiceClient, TelemetryClient telemetryClient, ILoggingRepository logger)
        {
            _graphServiceClient = graphServiceClient;
            _telemetryClient = telemetryClient;
            _loggingRepository = logger;
        }

        private const int MaxRetries = 10;
        private const int MaxResultCount = 999;

        private const string ResourceUnitHeader = "x-ms-resource-unit";
        private const string ThrottlePercentageHeader = "x-ms-throttle-limit-percentage";
        private const string ThrottleInfoHeader = "x-ms-throttle-information";
        private const string ThrottleScopeHeader = "x-ms-throttle-scope";

        public async Task<bool> GroupExists(Guid objectId)
        {
            try
            {
                var group = await _graphServiceClient.Groups[objectId.ToString()].Request().WithMaxRetry(MaxRetries).GetAsync();
                TrackMetrics(group.AdditionalData);
                return group != null;
            }
            catch (ServiceException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    return false;

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                throw;
            }
        }
        public async Task<bool> GroupExists(string groupName)
        {
            try
            {
                var groupCollectionPage = await _graphServiceClient.Groups.Request().Filter($"startswith(displayName, '{groupName}')").GetAsync();
                return groupCollectionPage.Count > 0;
            }
            catch (ServiceException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    return false;

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                throw;
            }
        }

        public async Task<AzureADGroup> GetGroup(string groupName)
        {
            try
            {
                var groupCollectionPage = await _graphServiceClient.Groups.Request().Filter($"startswith(displayName, '{groupName}')").GetAsync();
                return new AzureADGroup { ObjectId = new Guid(groupCollectionPage.CurrentPage[0].Id) };
            }
            catch (ServiceException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    return null;

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                return null;
            }
        }

        public async Task CreateGroup(string newGroupName)
        {
            try
            {
                if (await GroupExists(newGroupName))
                {
                    return;
                }

                var group = await _graphServiceClient.Groups.Request().AddAsync(new Group
                {
                    DisplayName = newGroupName,
                    Description = $"Integration test group: {newGroupName}",
                    MailNickname = new Guid().ToString(),
                    MailEnabled = false,
                    SecurityEnabled = true
                });
            }
            catch (Exception e)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Error creating group: {e}" });
            }
        }

        public async Task<List<AzureADUser>> GetTenantUsers(int userCount)
        {
            var tenantUsers = new HashSet<AzureADUser>();

            var userResponse = await _graphServiceClient.Users.Request().GetAsync();
            tenantUsers.UnionWith(userResponse.CurrentPage.Select(graphUser => new AzureADUser { ObjectId = new Guid(graphUser.Id) }));

            while (tenantUsers.Count < userCount)
            {
                userResponse = await userResponse.NextPageRequest.GetAsync();
                tenantUsers.UnionWith(userResponse.CurrentPage.Select(graphUser => new AzureADUser { ObjectId = new Guid(graphUser.Id) }));
            }

            return tenantUsers.ToList();
        }

        public async Task<bool> IsAppIDOwnerOfGroup(string appId, Guid groupObjectId)
        {
            if (await GroupExists(groupObjectId) == false) { return false; }

            // get the service principal ID by its app ID
            var servicePrincipal = (await _graphServiceClient.ServicePrincipals.Request().WithMaxRetry(MaxRetries).Filter($"appId eq '{appId}'").GetAsync()).Single();
            TrackMetrics(servicePrincipal.AdditionalData);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                RunId = RunId,
                Message = $"Checking if app ID {appId} (service principal with object ID {servicePrincipal.Id}) owns the group {groupObjectId}."
            });

            return await IsGroupOwnerAsync($"id eq '{servicePrincipal.Id}'", groupObjectId);
        }

        public async Task<bool> IsEmailRecipientOwnerOfGroupAsync(string email, Guid groupObjectId)
        {
            if (await GroupExists(groupObjectId) == false) { return false; }

            User user = null;

            try
            {
                user = await _graphServiceClient.Users[email].Request().GetAsync();
            }
            catch (ServiceException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    return false;

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                throw;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                RunId = RunId,
                Message = $"Checking if email owns the group {groupObjectId}."
            });

            return await IsGroupOwnerAsync($"id eq '{user.Id}'", groupObjectId);
        }

        public async Task<List<User>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                RunId = RunId,
                Message = $"Getting owners of group {groupObjectId}."
            });

            var owners = new List<User>();

            try
            {
                var request = _graphServiceClient.Groups[groupObjectId.ToString()].Owners
                                        .Request().WithMaxRetry(MaxRetries);

                if (top > 0) request = request.Top(top);

                var groupOwners = await request.GetAsync();
                owners.AddRange(groupOwners.CurrentPage.OfType<User>());
                TrackMetrics(groupOwners.AdditionalData);

                while (groupOwners.NextPageRequest != null)
                {
                    groupOwners = await groupOwners.NextPageRequest.WithMaxRetry(MaxRetries).GetAsync();
                    TrackMetrics(groupOwners.AdditionalData);
                    owners.AddRange(groupOwners.CurrentPage.OfType<User>());
                }

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = RunId,
                    Message = $"Retrieved{(top > 0 ? " top " : " ")}{owners.Count} owners of group {groupObjectId}."
                });

                return owners;
            }
            catch (ServiceException ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                throw;
            }
        }

        public async Task<string> GetGroupNameAsync(Guid objectId)
        {
            try
            {
                var group = await _graphServiceClient.Groups[objectId.ToString()].Request().WithMaxRetry(MaxRetries).GetAsync();
                TrackMetrics(group.AdditionalData);
                return group != null ? group.DisplayName : string.Empty;
            }
            catch (ServiceException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    return string.Empty;

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                throw;
            }
        }

        public async Task<List<AzureADUser>> GetUsersInGroupTransitively(Guid objectId)
        {
            var nonUserGraphObjects = new Dictionary<string, int>();
            IGroupTransitiveMembersCollectionWithReferencesPage members;

            try
            {
                members = await _graphServiceClient.Groups[objectId.ToString()].TransitiveMembers.Request()
                            .WithMaxRetry(MaxRetries)
                            .Select("id")
                            .GetAsync();
                TrackMetrics(members.AdditionalData);

                var toReturn = new List<AzureADUser>(ToUsers(members.CurrentPage, nonUserGraphObjects));
                while (members.NextPageRequest != null)
                {
                    members = await members.NextPageRequest.WithMaxRetry(MaxRetries).GetAsync();
                    TrackMetrics(members.AdditionalData);
                    toReturn.AddRange(ToUsers(members.CurrentPage, nonUserGraphObjects));
                }

                var nonUserGraphObjectsSummary = string.Join(Environment.NewLine, nonUserGraphObjects.Select(x => $"{x.Value}: {x.Key}"));
                await _loggingRepository.LogMessageAsync(new LogMessage { RunId = RunId, Message = $"From group {objectId}, read {toReturn.Count} users, and the following other directory objects:\n{nonUserGraphObjectsSummary}\n" });
                return toReturn;
            }
            catch (ServiceException ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                throw;
            }
        }

        /// <summary>
        /// get group members page by id.
        /// </summary>
        /// <param name="groupId">group id.</param>
        /// <returns>group members page.</returns>
        public async Task<IGroupTransitiveMembersCollectionWithReferencesPage> GetGroupMembersPageByIdAsync(string groupId)
        {
            var retryPolicy = GetRetryPolicy();
            return await retryPolicy.ExecuteAsync(async () =>
            {
                return await _graphServiceClient
                                .Groups[groupId]
                                .TransitiveMembers
                                .Request()
                                .Top(MaxResultCount)
                                .GetAsync();
            });
        }

        /// <summary>
        /// get group members page by next page url.
        /// </summary>
        /// <param name="groupMembersRef">group members page reference.</param>
        /// <param name="nextPageUrl">group members next page data link url.</param>
        /// <returns>group members page.</returns>
        public async Task<IGroupTransitiveMembersCollectionWithReferencesPage> GetGroupMembersNextPageAsnyc(
            IGroupTransitiveMembersCollectionWithReferencesPage groupMembersRef,
            string nextPageUrl)
        {
            var retryPolicy = GetRetryPolicy();
            return await retryPolicy.ExecuteAsync(async () =>
            {
                groupMembersRef.InitializeNextPageRequest(_graphServiceClient, nextPageUrl);
                return await groupMembersRef
                                .NextPageRequest
                                .GetAsync();
            });
        }

        public async Task<(List<AzureADUser> users,
                           Dictionary<string, int> nonUserGraphObjects,
                           string nextPageUrl,
                           IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)> GetFirstUsersPageAsync(Guid objectId)
        {
            var users = new List<AzureADUser>();
            var nonUserGraphObjects = new Dictionary<string, int>();

            var usersFromGroup = await GetGroupMembersPageByIdAsync(objectId.ToString());
            TrackMetrics(usersFromGroup.AdditionalData);
            usersFromGroup.AdditionalData.TryGetValue("@odata.nextLink", out object nextLink1);
            var nextPageUrl = (nextLink1 == null) ? string.Empty : nextLink1.ToString();
            users.AddRange(ToUsers(usersFromGroup, nonUserGraphObjects));
            return (users, nonUserGraphObjects, nextPageUrl, usersFromGroup);
        }

        public async Task<(List<AzureADUser> users,
                           Dictionary<string, int> nonUserGraphObjects,
                           string nextPageUrl,
                           IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)> GetNextUsersPageAsync(string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)
        {
            var users = new List<AzureADUser>();
            var nonUserGraphObjects = new Dictionary<string, int>();

            usersFromGroup = await GetGroupMembersNextPageAsnyc(usersFromGroup, nextPageUrl);
            TrackMetrics(usersFromGroup.AdditionalData);
            usersFromGroup.AdditionalData.TryGetValue("@odata.nextLink", out object nextLink2);
            nextPageUrl = (nextLink2 == null) ? string.Empty : nextLink2.ToString();
            users.AddRange(ToUsers(usersFromGroup, nonUserGraphObjects));
            return (users, nonUserGraphObjects, nextPageUrl, usersFromGroup);
        }

        public async Task<IEnumerable<IAzureADObject>> GetChildrenOfGroup(Guid objectId)
        {
            IGroupMembersCollectionWithReferencesPage members;

            try
            {
                members = await _graphServiceClient.Groups[objectId.ToString()].Members.Request()
                .WithMaxRetry(MaxRetries)
                .Select("id")
                .GetAsync();

                TrackMetrics(members.AdditionalData);

                var toReturn = new List<IAzureADObject>(ToEntities(members.CurrentPage));
                while (members.NextPageRequest != null)
                {
                    members = await members.NextPageRequest.WithMaxRetry(MaxRetries).GetAsync();
                    toReturn.AddRange(ToEntities(members.CurrentPage));
                }
                return toReturn;
            }
            catch (ServiceException ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = "Unable to retrieve group members.\n" + ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                throw;
            }
        }

        void TrackMetrics(IDictionary<string, object> additionalData)
        {
            // some replies just don't have the response headers
            // i suspect those either aren't throttled the same way or it's a different kind of call
            if (!additionalData.TryGetValue("responseHeaders", out var headers))
                return;

            // see https://github.com/microsoftgraph/msgraph-sdk-dotnet/blob/dev/docs/headers.md#reading-response-headers
            var responseHeaders = _graphServiceClient.HttpProvider.Serializer.DeserializeObject<Dictionary<string, List<string>>>(headers.ToString());

            if (responseHeaders.TryGetValue(ResourceUnitHeader, out var resourceValues))
                _telemetryClient.GetMetric(nameof(Metric.ResourceUnitsUsed)).TrackValue(ParseFirst<int>(resourceValues, int.TryParse));

            if (responseHeaders.TryGetValue(ThrottlePercentageHeader, out var throttleValues))
                _telemetryClient.GetMetric(nameof(Metric.ThrottleLimitPercentage)).TrackValue(ParseFirst<double>(throttleValues, double.TryParse));
        }

        const int GraphBatchLimit = 20;
        const int ConcurrentRequests = 10;
        public Task<(ResponseCode ResponseCode, int SuccessCount)> AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
        {
            //You can, in theory, send batches of 20 requests of 20 group adds each
            // but Graph starts saying "Service Unavailable" for a bunch of them if you do that, so only send so many at once
            // 5 seems to be the most without it starting to throw errors that have to be retried
            return BatchAndSend(users, b => MakeBulkAddRequest(b, targetGroup.ObjectId), GraphBatchLimit, 5);
        }

        public Task<(ResponseCode ResponseCode, int SuccessCount)> RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
        {
            // This, however, is the most we can send per delete batch, and it works pretty well.
            return BatchAndSend(users, b => MakeBulkRemoveRequest(b, targetGroup.ObjectId), 1, GraphBatchLimit);
        }

        delegate HttpRequestMessage MakeBulkRequest(List<AzureADUser> batch);
        private class ChunkOfUsers
        {
            public List<AzureADUser> ToSend { get; set; }
            public string Id { get; set; }

            private const int MaxBatchRetries = 5;

            // basically, whenever a batch is retried, we append the thread number after a dash
            public bool ShouldRetry => Id.Split('-')[1].Length < MaxBatchRetries;
            public ChunkOfUsers UpdateIdForRetry(int threadNumber)
            {
                Id += threadNumber;
                return this;
            }
        }

        private string GetNewChunkId() => $"{Guid.NewGuid().ToString().Replace("-", string.Empty)}-";

        private async Task<(ResponseCode ResponseCode, int SuccessCount)> BatchAndSend(IEnumerable<AzureADUser> users, MakeBulkRequest makeRequest, int requestMax, int batchSize)
        {
            if (!users.Any()) { return (ResponseCode.Ok, 0); }

            var queuedBatches = new ConcurrentQueue<ChunkOfUsers>(
                    ChunksOfSize(users, requestMax) // Chop up the users into chunks of how many per graph request (20 for add, 1 for remove)
                    .Select(x => new ChunkOfUsers { ToSend = x, Id = GetNewChunkId() }));

            var responses = await Task.WhenAll(Enumerable.Range(0, ConcurrentRequests).Select(x => ProcessQueue(queuedBatches, makeRequest, x, batchSize)));

            var status = responses.Any(x => x.ResponseCode == ResponseCode.Error) ? ResponseCode.Error : ResponseCode.Ok;
            return (status, responses.Sum(x => x.SuccessCount));
        }

        private async Task<(ResponseCode ResponseCode, int SuccessCount)> ProcessQueue(ConcurrentQueue<ChunkOfUsers> queue, MakeBulkRequest makeRequest, int threadNumber, int batchSize)
        {
            var successCount = 0;

            do
            {
                var toSend = new List<ChunkOfUsers>();
                while (queue.TryDequeue(out var step))
                {
                    toSend.Add(step);
                    if (toSend.Count == batchSize)
                    {
                        var response = await ProcessBatch(queue, toSend, makeRequest, threadNumber);
                        toSend.Clear();

                        successCount += response.SuccessCount;

                        if (response.ResponseCode == ResponseCode.Error)
                            return response;
                    }
                }

                if (toSend.Any())
                {
                    var response = await ProcessBatch(queue, toSend, makeRequest, threadNumber);

                    successCount += response.SuccessCount;

                    if (response.ResponseCode == ResponseCode.Error)
                        return response;
                }

            } while (!queue.IsEmpty); // basically, that last ProcessBatch may have put more stuff in the queue

            return (ResponseCode.Ok, successCount);
        }

        private async Task<(ResponseCode ResponseCode, int SuccessCount)> ProcessBatch(ConcurrentQueue<ChunkOfUsers> queue, List<ChunkOfUsers> toSend, MakeBulkRequest makeRequest, int threadNumber)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Thread number {threadNumber}: Sending a batch of {toSend.Count} requests.", RunId = RunId });
            int requeued = 0;
            bool hasUnrecoverableErrors = false;
            var successfulRequests = toSend.SelectMany(x => x.ToSend).ToList().Count;

            try
            {
                await foreach (var idToRetry in await SendBatch(new BatchRequestContent(toSend.Select(x => new BatchRequestStep(x.Id, makeRequest(x.ToSend))).ToArray())))
                {
                    var chunkToRetry = toSend.First(x => x.Id == idToRetry.RequestId);

                    successfulRequests -= chunkToRetry.ToSend.Count;

                    if (idToRetry.ResponseCode == ResponseCode.Error)
                    {
                        hasUnrecoverableErrors = true;
                        break;
                    }

                    if (chunkToRetry.ShouldRetry)
                    {
                        // Not found
                        if (chunkToRetry.ToSend.Count > 1
                            && !string.IsNullOrWhiteSpace(idToRetry.AzureObjectId))
                        {
                            var notFoundUser = chunkToRetry.ToSend.FirstOrDefault(x => x.ObjectId.ToString().Equals(idToRetry.AzureObjectId, StringComparison.InvariantCultureIgnoreCase));
                            if (notFoundUser != null)
                            {
                                chunkToRetry.ToSend.Remove(notFoundUser);

                                var notFoundChunk = new ChunkOfUsers
                                {
                                    Id = GetNewChunkId(),
                                    ToSend = new List<AzureADUser> { notFoundUser }
                                };

                                requeued++;
                                queue.Enqueue(notFoundChunk.UpdateIdForRetry(threadNumber));
                                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Queued {notFoundChunk.Id} from {chunkToRetry.Id}", RunId = RunId });
                            }
                        }

                        // Break down request for individual retries
                        if (chunkToRetry.ToSend.Count > 1 && idToRetry.ResponseCode == ResponseCode.IndividualRetry)
                        {
                            var chunksOfUsers = chunkToRetry.ToSend.Select(x => new ChunkOfUsers
                            {
                                Id = GetNewChunkId(),
                                ToSend = new List<AzureADUser> { x }
                            });

                            foreach (var chunk in chunksOfUsers)
                            {
                                requeued++;
                                queue.Enqueue(chunk.UpdateIdForRetry(threadNumber));
                                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Queued {chunk.Id} from {chunkToRetry.Id}", RunId = RunId });
                            }

                            chunkToRetry.ToSend.Clear();
                        }

                        if (chunkToRetry.ToSend.Count > 0)
                        {
                            requeued++;
                            var originalId = chunkToRetry.Id;
                            queue.Enqueue(chunkToRetry.UpdateIdForRetry(threadNumber));
                            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Requeued {originalId} as {chunkToRetry.Id}", RunId = RunId });
                        }
                    }
                }
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Thread number {threadNumber}: {toSend.Count - requeued} out of {toSend.Count} requests succeeded. {queue.Count} left.", RunId = RunId });
            }
            catch (ServiceException ex)
            {
                // winding up in here is a pretty rare event
                // Usually, it's because either a timeout happened or something else weird went on
                // the best thing to do is just requeue the chunks
                // but if a chunk has already been queued five times or so, drop it on the floor so we don't go forever
                // in the future, log the exception and which ones get dropped.

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                foreach (var chunk in toSend)
                {
                    if (chunk.ShouldRetry)
                    {
                        var originalId = chunk.Id;
                        queue.Enqueue(chunk.UpdateIdForRetry(threadNumber));

                        await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Requeued {originalId} as {chunk.Id}", RunId = RunId });
                    }
                }
            }

            var status = hasUnrecoverableErrors ? ResponseCode.Error : ResponseCode.Ok;
            return (status, successfulRequests);
        }

        private async Task<IAsyncEnumerable<RetryResponse>> SendBatch(BatchRequestContent tosend)
        {
            try
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Sending requests {string.Join(",", tosend.BatchRequestSteps.Keys)}.", RunId = RunId });

                var response = await _graphServiceClient.Batch.Request().PostAsync(tosend);
                return GetStepIdsToRetry(await response.GetResponsesAsync());
            }
            catch (ServiceException ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                throw;
            }
        }

        private static readonly HttpStatusCode[] _shouldRetry = new[] { HttpStatusCode.ServiceUnavailable, HttpStatusCode.GatewayTimeout, HttpStatusCode.BadGateway, HttpStatusCode.InternalServerError };
        private static readonly HttpStatusCode[] _isOkay = new[] { HttpStatusCode.NoContent, HttpStatusCode.NotFound, HttpStatusCode.OK };

        // These indicate that we're trying to remove a user that's already been removed.
        // Probably because an ID from earlier finally went through between the first try and the retry.
        private static readonly string[] _okayErrorMessages =
            {
                "One or more removed object references do not exist for the following modified properties: 'members'.",
                "One or more added object references already exist for the following modified properties: 'members'."
            };

        private static readonly Regex _userNotFound = new Regex(@"Resource '(?<id>[({]?[a-fA-F0-9]{8}[-]?([a-fA-F0-9]{4}[-]?){3}[a-fA-F0-9]{12}[})]?)' does not exist", RegexOptions.IgnoreCase);

        private async IAsyncEnumerable<RetryResponse> GetStepIdsToRetry(Dictionary<string, HttpResponseMessage> responses)
        {
            bool beenThrottled = false;

            var resourceUnitsUsed = _telemetryClient.GetMetric(nameof(Metric.ResourceUnitsUsed));
            var throttleLimitPercentage = _telemetryClient.GetMetric(nameof(Metric.ThrottleLimitPercentage));
            var writesUsed = _telemetryClient.GetMetric(nameof(Metric.WritesUsed));

            foreach (var kvp in responses)
            {
                //Ensure that the response messages get disposed of.
                using var response = kvp.Value;
                var status = response.StatusCode;
                var content = await response.Content.ReadAsStringAsync();

                if (response.Headers.TryGetValues(ResourceUnitHeader, out var resourceValues))
                    resourceUnitsUsed.TrackValue(ParseFirst<int>(resourceValues, int.TryParse));

                if (response.Headers.TryGetValues(ThrottlePercentageHeader, out var throttleValues))
                    throttleLimitPercentage.TrackValue(ParseFirst<double>(throttleValues, double.TryParse));

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Response - RequestId:{kvp.Key} - StatusCode:{status} - Content:{content}",
                    RunId = RunId
                });


                // Note that the ones with empty bodies mean "this response is okay and we don't have to do anything about it."
                if (status == HttpStatusCode.BadRequest && IsOkayError(content)) { }
                else if (status == HttpStatusCode.NotFound && (content).Contains("does not exist or one of its queried reference-property objects are not present."))
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"Regex Expression: {_userNotFound} and Content: {content}",
                        RunId = RunId
                    });

                    var match = _userNotFound.Match(content);
                    var userId = default(string);

                    if (match.Success)
                    {
                        userId = match.Groups["id"].Value;
                        await _loggingRepository.LogMessageAsync(new LogMessage
                        {
                            Message = $"User ID is found",
                            RunId = RunId
                        });
                    }

                    else
                    {
                        await _loggingRepository.LogMessageAsync(new LogMessage
                        {
                            Message = $"User ID is missing",
                            RunId = RunId
                        });

                        yield return new RetryResponse
                        {
                            RequestId = kvp.Key,
                            ResponseCode = ResponseCode.Ok
                        };
                    }

                    yield return new RetryResponse
                    {
                        RequestId = kvp.Key,
                        ResponseCode = ResponseCode.Ok,
                        AzureObjectId = userId
                    };

                }
                else if (_isOkay.Contains(status)) { writesUsed.TrackValue(1); }
                else if (status == HttpStatusCode.TooManyRequests)
                {
                    // basically, each request in the batch will probably say it's been throttled
                    // but we only need to wait the first time.
                    // this isn't strictly true- i believe that the count gets reset if any other threads send requests
                    // but it's true enough until we can engineer something more robust

                    if (!beenThrottled)
                    {
                        // basically, go ahead and start waiting while we log the throttling info
                        // add a few seconds to account for other 419s that happen before we can send the signal to pause.
                        var throttleWait = CalculateThrottleWait(response.Headers.RetryAfter) + TimeSpan.FromSeconds(10);

                        var startThrottling = Task.Delay(throttleWait);
                        var gotThrottleInfo = response.Headers.TryGetValues(ThrottleInfoHeader, out var throttleInfo);
                        var gotThrottleScope = response.Headers.TryGetValues(ThrottleScopeHeader, out var throttleScope);
                        await _loggingRepository.LogMessageAsync(new LogMessage
                        {
                            Message = string.Format("Got 429 throttled. Waiting {0} seconds. Delta: {1} Date: {2} Reason: {3} Scope: {4}",
                                throttleWait.TotalSeconds,
                                response.Headers.RetryAfter.Delta != null ? response.Headers.RetryAfter.Delta.ToString() : "(none)",
                                response.Headers.RetryAfter.Date != null ? response.Headers.RetryAfter.Date.ToString() : "(none)",
                                gotThrottleInfo ? string.Join(',', throttleInfo) : "(none)",
                                gotThrottleScope ? string.Join(',', throttleScope) : "(none)"),
                            RunId = RunId
                        });
                        await startThrottling;
                        beenThrottled = true;
                    }

                    // it's possible for only some requests in a batch to be throttled, so only retry the ones that were throttled.
                    yield return new RetryResponse
                    {
                        RequestId = kvp.Key,
                        ResponseCode = ResponseCode.IndividualRetry
                    };
                }
                else if (status == HttpStatusCode.Forbidden && content.Contains("Guests users are not allowed to join"))
                {
                    yield return new RetryResponse
                    {
                        RequestId = kvp.Key,
                        ResponseCode = ResponseCode.IndividualRetry
                    };
                }
                else if (_shouldRetry.Contains(status))
                {
                    yield return new RetryResponse
                    {
                        RequestId = kvp.Key,
                        ResponseCode = ResponseCode.Ok
                    };
                }
                else
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Got an unexpected error from Graph, stopping all processing for current job: {status} {response.ReasonPhrase} {content}.", RunId = RunId });
                    yield return new RetryResponse
                    {
                        RequestId = kvp.Key,
                        ResponseCode = ResponseCode.Error
                    };
                }
            }
        }

        delegate bool TryParseFunction<T>(string str, out T parsed);
        private static T ParseFirst<T>(IEnumerable<string> toParse, TryParseFunction<T> tryParse)
        {
            foreach (var str in toParse)
            {
                if (tryParse(str, out var parsed))
                {
                    return parsed;
                }
            }

            return default;
        }

        private static bool IsOkayError(string error)
        {
            error = JObject.Parse(error)["error"]["message"].Value<string>();
            return _okayErrorMessages.Any(x => error.Contains(x));
        }
        private static TimeSpan CalculateThrottleWait(RetryConditionHeaderValue wait)
        {
            // we're much more likely to hit the write quota, so default to the 2 minute and 30 second wait
            // https://docs.microsoft.com/en-us/graph/throttling#pattern
            TimeSpan waitFor = TimeSpan.FromSeconds(150);
            if (wait.Delta.HasValue) { waitFor = wait.Delta.Value; }
            if (wait.Date.HasValue) { waitFor = wait.Date.Value - DateTimeOffset.UtcNow; }
            return waitFor;
        }

        private HttpRequestMessage MakeBulkAddRequest(List<AzureADUser> batch, Guid targetGroup)
        {
            return new HttpRequestMessage(HttpMethod.Patch, $"https://graph.microsoft.com/v1.0/groups/{targetGroup}")
            {
                Content = new StringContent(MakeAddRequestBody(batch), System.Text.Encoding.UTF8, "application/json"),
            };
        }

        private HttpRequestMessage MakeBulkRemoveRequest(List<AzureADUser> batch, Guid targetGroup)
        {
            // You have to remove users with their object ID. UPN won't work because you can only use it when the thing you're removing is
            // unambiguously a user.

            if (batch.Count != 1) { throw new ArgumentException("Batches of deletes must have exactly one item. This one has " + batch.Count); }

            var toRemove = batch.Single().ObjectId;
            return new HttpRequestMessage(HttpMethod.Delete, $"https://graph.microsoft.com/v1.0/groups/{targetGroup}/members/{toRemove}/$ref");
        }

        private static string MakeAddRequestBody(List<AzureADUser> users)
        {
            JObject body = new JObject
            {
                ["members@odata.bind"] = JArray.FromObject(users.Select(x => $"https://graph.microsoft.com/v1.0/users/{x.ObjectId}"))
            };
            return body.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static IEnumerable<List<T>> ChunksOfSize<T>(IEnumerable<T> enumerable, int chunkSize)
        {
            var toReturn = new List<T>();
            foreach (var item in enumerable)
            {
                if (toReturn.Count == chunkSize)
                {
                    yield return toReturn;
                    toReturn = new List<T>();
                }
                toReturn.Add(item);
            }
            yield return toReturn;
        }

        private IEnumerable<IAzureADObject> ToEntities(IEnumerable<DirectoryObject> fromGraph)
        {
            foreach (var directoryObj in fromGraph)
            {
                switch (directoryObj)
                {
                    case User user:
                        yield return new AzureADUser { ObjectId = Guid.Parse(user.Id) };
                        break;
                    case Group group:
                        yield return new AzureADGroup { ObjectId = Guid.Parse(group.Id) };
                        break;
                    default:
                        break;
                }
            }
        }

        private IEnumerable<AzureADUser> ToUsers(IEnumerable<DirectoryObject> fromGraph, Dictionary<string, int> nonUserGraphObjects)
        {
            foreach (var directoryObj in fromGraph)
            {
                switch (directoryObj)
                {
                    case User user:
                        yield return new AzureADUser { ObjectId = Guid.Parse(user.Id) };
                        break;
                    // We only care about users
                    // I'd prefer to be able to filter these out from the results on Graph's side, but the library doesn't support that yet.
                    // we do want to log the count of non-user graph objects, though
                    default:
                        if (nonUserGraphObjects.TryGetValue(directoryObj.ODataType, out int count))
                            nonUserGraphObjects[directoryObj.ODataType] = count + 1;
                        else
                            nonUserGraphObjects[directoryObj.ODataType] = 1;
                        break;
                }
            }
        }

        private async Task<bool> IsGroupOwnerAsync(string query, Guid groupObjectId)
        {
            try
            {
                var groupOwners = await _graphServiceClient.Groups[groupObjectId.ToString()].Owners.Request().WithMaxRetry(MaxRetries).Filter(query).GetAsync();
                TrackMetrics(groupOwners.AdditionalData);
                return groupOwners.Any();
            }
            catch (ServiceException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    return false;

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                throw;
            }
        }

        private AsyncRetryPolicy GetRetryPolicy()
        {
            var retryLimit = 4;
            var retryPolicy = Policy.Handle<ServiceException>()
                    .WaitAndRetryAsync(
                       retryCount: retryLimit,
                       retryAttempt => TimeSpan.FromMinutes(2),
                       onRetry: async (ex, waitTime, currentRetry, context) =>
                       {
                           await _loggingRepository.LogMessageAsync(new LogMessage
                           {
                               Message = $"Got a transient exception. Retrying. This was try {currentRetry} out of {retryLimit}.\n{ex}"
                           });
                       }
                    );

            return retryPolicy;
        }
    }

    internal class RetryResponse
    {
        public string RequestId { get; set; }
        public ResponseCode ResponseCode { get; set; }
        public string AzureObjectId { get; set; }
    }
}

