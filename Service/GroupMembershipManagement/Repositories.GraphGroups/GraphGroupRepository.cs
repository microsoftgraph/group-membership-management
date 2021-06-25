// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Graph;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Repositories.GraphGroups
{
    public class GraphGroupRepository : IGraphGroupRepository
    {
        private readonly IGraphServiceClient _graphServiceClient;
        private readonly ILoggingRepository _log;

        public Guid RunId { get; set; }

        public GraphGroupRepository(IGraphServiceClient graphServiceClient, ILoggingRepository logger)
        {
            _graphServiceClient = graphServiceClient;
            _log = logger;
        }

        private const int MaxRetries = 10;
        private const int MaxResultCount = 999;

        public async Task<bool> GroupExists(Guid objectId)
        {
            try
            {
                var group = await _graphServiceClient.Groups[objectId.ToString()].Request().WithMaxRetry(MaxRetries).GetAsync();
                return group != null;
            }
            catch (ServiceException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    return false;

                await _log.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                throw;
            }
        }

        public async Task<bool> IsAppIDOwnerOfGroup(string appId, Guid groupObjectId)
        {
            if (await GroupExists(groupObjectId) == false) { return false; }

            // get the service principal ID by its app ID
            var servicePrincipal = (await _graphServiceClient.ServicePrincipals.Request().WithMaxRetry(MaxRetries).Filter($"appId eq '{appId}'").GetAsync()).Single();

            await _log.LogMessageAsync(new LogMessage
            {
                RunId = RunId,
                Message = $"Checking if app ID {appId} (service principal with object ID {servicePrincipal.Id}) owns the group {groupObjectId}."
            });

            try
            {
                var groupOwners = await _graphServiceClient.Groups[groupObjectId.ToString()].Owners.Request().WithMaxRetry(MaxRetries).Filter($"id eq '{servicePrincipal.Id}'").GetAsync();
                return groupOwners.Any();
            }
            catch (ServiceException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    return false;

                await _log.LogMessageAsync(new LogMessage
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
                return group != null ? group.DisplayName : string.Empty;
            }
            catch (ServiceException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    return string.Empty;

                await _log.LogMessageAsync(new LogMessage
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

                var toReturn = new List<AzureADUser>(ToUsers(members.CurrentPage, nonUserGraphObjects));
                while (members.NextPageRequest != null)
                {
                    members = await members.NextPageRequest.WithMaxRetry(MaxRetries).GetAsync();
                    toReturn.AddRange(ToUsers(members.CurrentPage, nonUserGraphObjects));
                }

                var nonUserGraphObjectsSummary = string.Join(Environment.NewLine, nonUserGraphObjects.Select(x => $"{x.Value}: {x.Key}"));
                await _log.LogMessageAsync(new LogMessage { RunId = RunId, Message = $"From group {objectId}, read {toReturn.Count} users, and the following other directory objects:\n{nonUserGraphObjectsSummary}\n" });
                return toReturn;
            }
            catch (ServiceException ex)
            {
                await _log.LogMessageAsync(new LogMessage
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
            return await _graphServiceClient
                                    .Groups[groupId]
                                    .TransitiveMembers
                                    .Request()
                                    .Top(MaxResultCount)
                                    .WithMaxRetry(MaxRetries)
                                    .GetAsync();
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
            groupMembersRef.InitializeNextPageRequest(_graphServiceClient, nextPageUrl);
            return await groupMembersRef
                .NextPageRequest
                .GetAsync();
        }

        public async Task<(List<AzureADUser> users,
                           Dictionary<string, int> nonUserGraphObjects,
                           string nextPageUrl,
                           IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)> GetFirstUsersPageAsync(Guid objectId)
        {
            var users = new List<AzureADUser>();
            var nonUserGraphObjects = new Dictionary<string, int>();

            var usersFromGroup = await GetGroupMembersPageByIdAsync(objectId.ToString());
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
                await _log.LogMessageAsync(new LogMessage
                {
                    Message = "Unable to retrieve group members.\n" + ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                throw;
            }
        }

        const int GraphBatchLimit = 20;
        const int ConcurrentRequests = 10;
        public Task<ResponseCode> AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
        {
            //You can, in theory, send batches of 20 requests of 20 group adds each
            // but Graph starts saying "Service Unavailable" for a bunch of them if you do that, so only send so many at once
            // 5 seems to be the most without it starting to throw errors that have to be retried
            return BatchAndSend(users, b => MakeBulkAddRequest(b, targetGroup.ObjectId), GraphBatchLimit, 5);
        }

        public Task<ResponseCode> RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
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

        private async Task<ResponseCode> BatchAndSend(IEnumerable<AzureADUser> users, MakeBulkRequest makeRequest, int requestMax, int batchSize)
        {
            if (!users.Any()) { return ResponseCode.Ok; }

            int batchRequestId = 0;
            var queuedBatches = new ConcurrentQueue<ChunkOfUsers>(
                    ChunksOfSize(users, requestMax) // Chop up the users into chunks of how many per graph request (20 for add, 1 for remove)
                    .Select(x => new ChunkOfUsers { ToSend = x, Id = $"{batchRequestId++}-" }));

            var responses = await Task.WhenAll(Enumerable.Range(0, ConcurrentRequests).Select(x => ProcessQueue(queuedBatches, makeRequest, x, batchSize)));

            return responses.Any(x => x == ResponseCode.Error) ? ResponseCode.Error : ResponseCode.Ok;
        }

        private async Task<ResponseCode> ProcessQueue(ConcurrentQueue<ChunkOfUsers> queue, MakeBulkRequest makeRequest, int threadNumber, int batchSize)
        {
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

                        if (response == ResponseCode.Error)
                            return response;
                    }
                }

                if (toSend.Any())
                {
                    var response = await ProcessBatch(queue, toSend, makeRequest, threadNumber);

                    if (response == ResponseCode.Error)
                        return response;
                }

            } while (!queue.IsEmpty); // basically, that last ProcessBatch may have put more stuff in the queue

            return ResponseCode.Ok;
        }

        private async Task<ResponseCode> ProcessBatch(ConcurrentQueue<ChunkOfUsers> queue, List<ChunkOfUsers> toSend, MakeBulkRequest makeRequest, int threadNumber)
        {
            await _log.LogMessageAsync(new LogMessage { Message = $"Thread number {threadNumber}: Sending a batch of {toSend.Count} requests.", RunId = RunId });
            int requeued = 0;
            bool hasUnrecoverableErrors = false;

            try
            {
                await foreach (var idToRetry in await SendBatch(new BatchRequestContent(toSend.Select(x => new BatchRequestStep(x.Id, makeRequest(x.ToSend))).ToArray())))
                {
                    if (idToRetry.ResponseCode == ResponseCode.Error)
                    {
                        hasUnrecoverableErrors = true;
                        break;
                    }

                    requeued++;
                    var chunkToRetry = toSend.First(x => x.Id == idToRetry.Id);
                    if (chunkToRetry.ShouldRetry)
                    {
                        queue.Enqueue(chunkToRetry.UpdateIdForRetry(threadNumber));
                    }
                }
                await _log.LogMessageAsync(new LogMessage { Message = $"Thread number {threadNumber}: {toSend.Count - requeued} out of {toSend.Count} requests succeeded. {queue.Count} left.", RunId = RunId });
            }
            catch (ServiceException ex)
            {
                // winding up in here is a pretty rare event
                // Usually, it's because either a timeout happened or something else weird went on
                // the best thing to do is just requeue the chunks
                // but if a chunk has already been queued five times or so, drop it on the floor so we don't go forever
                // in the future, log the exception and which ones get dropped.

                await _log.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                foreach (var chunk in toSend)
                {
                    if (chunk.ShouldRetry)
                        queue.Enqueue(chunk.UpdateIdForRetry(threadNumber));
                }
            }

            return hasUnrecoverableErrors ? ResponseCode.Error : ResponseCode.Ok;
        }

        private async Task<IAsyncEnumerable<(string Id, ResponseCode ResponseCode)>> SendBatch(BatchRequestContent tosend)
        {
            try
            {
                var response = await _graphServiceClient.Batch.Request().WithMaxRetry(MaxRetries).PostAsync(tosend);
                return GetStepIdsToRetry(await response.GetResponsesAsync());
            }
            catch (ServiceException ex)
            {
                await _log.LogMessageAsync(new LogMessage
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

        private async IAsyncEnumerable<(string Id, ResponseCode ResponseCode)> GetStepIdsToRetry(Dictionary<string, HttpResponseMessage> responses)
        {
            bool beenThrottled = false;
            foreach (var kvp in responses)
            {
                //Ensure that the response messages get disposed of.
                using var response = kvp.Value;

                var status = response.StatusCode;

                string badRequestBody = string.Empty;
                // Note that the ones with empty bodies mean "this response is okay and we don't have to do anything about it."
                if (status == HttpStatusCode.BadRequest && IsOkayError(badRequestBody = await response.Content.ReadAsStringAsync())) { }
                else if (status == HttpStatusCode.NotFound && (await response.Content.ReadAsStringAsync()).Contains("does not exist or one of its queried reference-property objects are not present."))
                {
                    // basically, the graph sometimes won't be able to find the group for some reason, and we have to retry then
                    // but sometimes that error doesn't go away, because the thing's already been removed, so cap the number of retries.
                    // I think the more accurate rule is something like "retry when adding to the group, ignore when removing from the group",
                    // but since this function, by design, doesn't know which kind of thing it's doing, this is a reasonable compromise.
                    // every batch is capped by number of retries, so we just have to retry here.
                    yield return (kvp.Key, ResponseCode.Ok);
                }
                else if (_isOkay.Contains(status)) { }
                else if (status == HttpStatusCode.TooManyRequests)
                {
                    // basically, each request in the batch will probably say it's been throttled
                    // but we only need to wait the first time.
                    if (!beenThrottled)
                    {
                        await HandleThrottling(response.Headers.RetryAfter);
                        beenThrottled = true;
                    }
                    yield return (kvp.Key, ResponseCode.Ok);
                }
                else if (_shouldRetry.Contains(status)) { yield return (kvp.Key, ResponseCode.Ok); }
                else
                {
                    badRequestBody = await response.Content.ReadAsStringAsync();
                    await _log.LogMessageAsync(new LogMessage { Message = $"Got an unexpected error from Graph, stopping all processing for current job: {status} {response.ReasonPhrase} {badRequestBody}.", RunId = RunId });
                    yield return (kvp.Key, ResponseCode.Error);
                }
            }
        }

        private static bool IsOkayError(string error)
        {
            error = JObject.Parse(error)["error"]["message"].Value<string>();
            return _okayErrorMessages.Any(x => error.Contains(x));
        }
        private static Task HandleThrottling(RetryConditionHeaderValue wait)
        {
            TimeSpan waitFor = TimeSpan.FromSeconds(120);
            if (wait.Delta.HasValue) { waitFor = wait.Delta.Value; }
            if (wait.Date.HasValue) { waitFor = wait.Date.Value - DateTimeOffset.UtcNow; }
            return Task.Delay(waitFor);
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
    }
}
