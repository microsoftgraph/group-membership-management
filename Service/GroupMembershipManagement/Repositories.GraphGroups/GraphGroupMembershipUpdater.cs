// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.Constants;
using Repositories.Contracts.Helpers;
using Services.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Repositories.GraphGroups
{
    internal class GraphGroupMembershipUpdater : GraphGroupRepositoryBase
    {
        private const int GraphBatchLimit = 20;
        private readonly int _concurrentAddRequests;
        private readonly int _concurrentRemoveRequests;
        private readonly ILogger<GraphGroupMembershipUpdater> _graphGroupMembershipUpdaterLogger;

        private static readonly HttpStatusCode[] _shouldRetry = new[]
            { HttpStatusCode.ServiceUnavailable, HttpStatusCode.GatewayTimeout, HttpStatusCode.BadGateway, HttpStatusCode.InternalServerError };
        private static readonly HttpStatusCode[] _isOkay = new[]
            { HttpStatusCode.NoContent, HttpStatusCode.NotFound, HttpStatusCode.OK };

        // These indicate that we're trying to remove a user that's already been removed.
        // Probably because an ID from earlier finally went through between the first try and the retry.
        private static readonly string _notFoundResponseError = "One or more removed object references do not exist for the following modified properties: 'members'.";
        private static readonly string _alreadyExistsResponseError = "One or more added object references already exist for the following modified properties: 'members'.";

        private delegate HttpRequestMessage MakeBulkRequest(List<AzureADUser> batch);

        private ConcurrentBag<AzureADUser> _usersNotFound = new ConcurrentBag<AzureADUser>();
        private ConcurrentBag<AzureADUser> _usersAlreadyExist = new ConcurrentBag<AzureADUser>();

        public Guid? RunId { get; set; }

        public GraphGroupMembershipUpdater(GraphServiceClient graphServiceClient,
                                           GraphGroupMetricTracker graphGroupMetricTracker,
                                           IGraphRepositorySettings graphRepositorySettings,
                                           ILogger<GraphGroupMembershipUpdater> graphGroupMembershipUpdaterLogger)
                                           : base(graphServiceClient, graphGroupMembershipUpdaterLogger, graphGroupMetricTracker)
        {
            _concurrentAddRequests = graphRepositorySettings == null ? 10 : graphRepositorySettings.ConcurrentAddRequests;
            _concurrentRemoveRequests = graphRepositorySettings == null ? 10 : graphRepositorySettings.ConcurrentRemoveRequests;
            _graphGroupMembershipUpdaterLogger = graphGroupMembershipUpdaterLogger ?? throw new ArgumentNullException(nameof(graphGroupMembershipUpdaterLogger));
        }


        public Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)>
            AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
        {
            //You can, in theory, send batches of 20 requests of 20 group adds each
            // but Graph starts saying "Service Unavailable" for a bunch of them if you do that, so only send so many at once
            // 5 seems to be the most without it starting to throw errors that have to be retried
            return BatchAndSend(users, b => MakeBulkAddRequest(b, targetGroup.ObjectId), GraphBatchLimit, 5, targetGroup.ObjectId, _concurrentAddRequests);
        }

        private HttpRequestMessage MakeBulkAddRequest(List<AzureADUser> batch, Guid targetGroup)
        {
            return new HttpRequestMessage(HttpMethod.Patch, $"https://graph.microsoft.com/v1.0/groups/{targetGroup}")
            {
                Content = new StringContent(MakeAddRequestBody(batch), System.Text.Encoding.UTF8, "application/json"),
            };
        }

        private static string MakeAddRequestBody(List<AzureADUser> users)
        {
            var body = new JsonObject
            {
                ["members@odata.bind"] = new JsonArray(users.Select(x => JsonValue.Create($"https://graph.microsoft.com/v1.0/users/{x.ObjectId}")).ToArray())
            };

            return body.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }

        public Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)>
            RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
        {
            // This, however, is the most we can send per delete batch, and it works pretty well.
            return BatchAndSend(users, b => MakeBulkRemoveRequest(b, targetGroup.ObjectId), 1, GraphBatchLimit, targetGroup.ObjectId, _concurrentRemoveRequests);
        }

        private HttpRequestMessage MakeBulkRemoveRequest(List<AzureADUser> batch, Guid targetGroup)
        {
            // You have to remove users with their object ID. UPN won't work because you can only use it when the thing you're removing is
            // unambiguously a user.

            if (batch.Count != 1) { throw new ArgumentException("Batches of deletes must have exactly one item. This one has " + batch.Count); }

            var toRemove = batch.Single().ObjectId;
            return new HttpRequestMessage(HttpMethod.Delete, $"https://graph.microsoft.com/v1.0/groups/{targetGroup}/members/{toRemove}/$ref");
        }

        private string GetNewChunkId() => $"{Guid.NewGuid().ToString().Replace("-", string.Empty)}";

        private async Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)>
            BatchAndSend(IEnumerable<AzureADUser> users, MakeBulkRequest makeRequest, int requestMax, int batchSize, Guid targetGroupId, int concurrentRequests)
        {
            if (!users.Any()) { return (ResponseCode.Ok, 0, new List<AzureADUser>(), new List<AzureADUser>()); }

            var queuedBatches = new ConcurrentQueue<ChunkOfUsers>(
                    users.Chunk(requestMax) // Chop up the users into chunks of how many per graph request (20 for add, 1 for remove)
                    .Select(x => new ChunkOfUsers
                    {
                        ToSend = x.ToList(),
                        Id = x[0].MembershipAction == MembershipAction.Add ? GetNewChunkId() : x[0].ObjectId.ToString()
                    }));

            var responses = await Task.WhenAll(Enumerable.Range(0, concurrentRequests).Select(x => ProcessQueue(queuedBatches, makeRequest, x, batchSize, targetGroupId)));
            var status = responses.Any(x => x.ResponseCode == ResponseCode.GuestError) ?
                ResponseCode.GuestError :
                (responses.Any(x => x.ResponseCode == ResponseCode.Error) ?
                    ResponseCode.Error :
                    ResponseCode.Ok);

            return (status, responses.Sum(x => x.SuccessCount), _usersNotFound.ToList(), _usersAlreadyExist.ToList());
        }

        private async Task<(ResponseCode ResponseCode, int SuccessCount)> ProcessQueue(ConcurrentQueue<ChunkOfUsers> queue, MakeBulkRequest makeRequest, int threadNumber, int batchSize, Guid targetGroupId)
        {
            var successCount = 0;
            var maxNumberOfRequests = batchSize * GraphBatchLimit;

            do
            {
                var toSend = new List<ChunkOfUsers>();
                while (queue.TryDequeue(out var step))
                {
                    var isUnderMaxLimit = toSend.Sum(x => x.ToSend.Count) + step.ToSend.Count <= maxNumberOfRequests;

                    if (isUnderMaxLimit && toSend.Count < GraphBatchLimit)
                        toSend.Add(step);
                    else
                    {
                        queue.Enqueue(step);
                        var response = await ProcessBatch(queue, toSend, makeRequest, threadNumber, targetGroupId);
                        toSend.Clear();

                        successCount += response.SuccessCount;

                        if (response.ResponseCode == ResponseCode.Error)
                            return response;
                    }
                }

                if (toSend.Any())
                {
                    var response = await ProcessBatch(queue, toSend, makeRequest, threadNumber, targetGroupId);

                    successCount += response.SuccessCount;

                    if (response.ResponseCode == ResponseCode.Error || response.ResponseCode == ResponseCode.GuestError)
                        return response;
                }

            } while (!queue.IsEmpty); // basically, that last ProcessBatch may have put more stuff in the queue

            return (ResponseCode.Ok, successCount);
        }

        private async Task<(ResponseCode ResponseCode, int SuccessCount)> ProcessPatchBatch(ConcurrentQueue<ChunkOfUsers> queue, List<ChunkOfUsers> toSend, MakeBulkRequest makeRequest, int threadNumber)
        {
            _graphGroupMembershipUpdaterLogger.LogDebugWithRunId(RunId, $"Thread number {threadNumber}: Sending a batch of {toSend.Count} requests.");
            int requeued = 0;
            bool hasUnrecoverableErrors = false;
            var successfulRequests = toSend.Where(x => !x.SendAsPostRequest).SelectMany(x => x.ToSend).ToList().Count;
            var guestUserError = false;

            try
            {
                var patchRequests = toSend.Where(x => !x.SendAsPostRequest).ToList();
                if (!patchRequests.Any())
                    return (ResponseCode.Ok, 0);

                var batchRequestContent = new BatchRequestContentCollection(_graphServiceClient);
                patchRequests.ForEach(x => batchRequestContent.AddBatchRequestStep(new BatchRequestStep(x.Id, makeRequest(x.ToSend))));

                foreach (var idToRetry in await SendBatch(batchRequestContent))
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
                        chunkToRetry.UpdateIdForRetry();

                        // Not found
                        if (!string.IsNullOrWhiteSpace(idToRetry.AzureObjectId))
                        {
                            var notFoundUser = chunkToRetry.ToSend.FirstOrDefault(x => x.ObjectId.ToString().Equals(idToRetry.AzureObjectId, StringComparison.InvariantCultureIgnoreCase));
                            if (notFoundUser != null)
                            {
                                chunkToRetry.ToSend.Remove(notFoundUser);
                            }

                            if (chunkToRetry.ToSend.Count == 1 && chunkToRetry.ToSend[0].MembershipAction == MembershipAction.Remove)
                            {
                                continue;
                            }
                        }

                        // Flag for individual retries
                        // It will be immediately retried within ProcessPostBatch
                        if (chunkToRetry.ToSend.Count > 1
                            && (idToRetry.ResponseCode == ResponseCode.IndividualRetry
                                || idToRetry.ResponseCode == ResponseCode.IndividualRetryAlreadyExists
                                || idToRetry.ResponseCode == ResponseCode.GuestError
                               ))
                        {
                            chunkToRetry.SendAsPostRequest = true;
                            _graphGroupMembershipUpdaterLogger.LogInformationWithRunId(RunId, $"Set {chunkToRetry.Id} as POST request");
                            continue;
                        }

                        if (chunkToRetry.ToSend.Count > 0)
                        {
                            if (chunkToRetry.ToSend.Count == 1 && idToRetry.HttpStatusCode == HttpStatusCode.BadRequest)
                            {
                                if (idToRetry.ResponseCode == ResponseCode.IndividualRetryAlreadyExists)
                                {
                                    _graphGroupMembershipUpdaterLogger.LogInformationWithRunId(RunId, $"{chunkToRetry.Id} already exists");

                                    _usersAlreadyExist.Add(chunkToRetry.ToSend[0]);
                                }
                                else
                                {
                                    _graphGroupMembershipUpdaterLogger.LogInformationWithRunId(RunId, $"{chunkToRetry.Id} was not removed as it could not be found");
                                }
                            }
                            else if (chunkToRetry.ToSend.Count == 1 && idToRetry.HttpStatusCode == HttpStatusCode.NotFound)
                            {
                                // Single-user PATCH that got NotFound - user doesn't exist, no point retrying
                                _graphGroupMembershipUpdaterLogger.LogInformationWithRunId(RunId, $"Adding {chunkToRetry.ToSend[0].ObjectId} failed as this resource does not exist.");

                                _usersNotFound.Add(chunkToRetry.ToSend[0]);
                            }
                            else if (chunkToRetry.ToSend.Count == 1 && idToRetry.HttpStatusCode == HttpStatusCode.Forbidden && idToRetry.ResponseCode == ResponseCode.GuestError)
                            {
                                _graphGroupMembershipUpdaterLogger.LogInformationWithRunId(RunId, $"{chunkToRetry.ToSend[0].ObjectId} was not added because it is a guest user and the destination does not allow guest users");
                                guestUserError = true;
                            }
                            else
                            {
                                requeued++;
                                queue.Enqueue(chunkToRetry);
                                _graphGroupMembershipUpdaterLogger.LogInformationWithRunId(RunId, $"Requeued {chunkToRetry.Id}-{chunkToRetry.RetryCount}");
                            }
                        }
                    }
                }
                _graphGroupMembershipUpdaterLogger.LogDebugWithRunId(RunId, $"Thread number {threadNumber}: {toSend.Count - requeued} out of {toSend.Count} requests succeeded. {queue.Count} left.");
            }
            catch (ServiceException ex)
            {
                // winding up in here is a pretty rare event
                // Usually, it's because either a timeout happened or something else weird went on
                // the best thing to do is just requeue the chunks
                // but if a chunk has already been queued five times or so, drop it on the floor so we don't go forever
                // in the future, log the exception and which ones get dropped.

                _graphGroupMembershipUpdaterLogger.LogErrorWithRunId(RunId, ex.GetBaseException().ToString(), ex);

                foreach (var chunk in toSend)
                {
                    if (chunk.ShouldRetry)
                    {
                        var originalId = chunk.Id;
                        queue.Enqueue(chunk.UpdateIdForRetry());

                        _graphGroupMembershipUpdaterLogger.LogInformationWithRunId(RunId, $"Requeued {originalId}-{chunk.RetryCount} ");
                    }
                }
            }

            var status = guestUserError ? ResponseCode.GuestError : (hasUnrecoverableErrors ? ResponseCode.Error : ResponseCode.Ok);
            return (status, successfulRequests);
        }

        private async Task<(ResponseCode ResponseCode, int SuccessCount)> ProcessPostBatch(ConcurrentQueue<ChunkOfUsers> queue, List<ChunkOfUsers> toSend, Guid targetGroupId)
        {
            (ResponseCode ResponseCode, int SuccessCount) postResponse = (ResponseCode.Ok, 0);
            var successfulRequests = 0;
            var postRequests = toSend.Where(x => x.SendAsPostRequest && x.ShouldRetry).ToList();
            if (postRequests.Count > 0)
            {
                foreach (var request in postRequests)
                {
                    successfulRequests += request.ToSend.Count;

                    var responses = await SendBatch(CreatePOSTBatchRequestContent(request, targetGroupId));
                    foreach (var response in responses)
                    {
                        if (response.ResponseCode != ResponseCode.Ok)
                            successfulRequests--;

                        if (response.HttpStatusCode == HttpStatusCode.NotFound
                            || response.HttpStatusCode == HttpStatusCode.BadRequest
                            || response.HttpStatusCode == HttpStatusCode.Forbidden
                            )
                        {
                            var stepToRemove = request.ToSend.FirstOrDefault(x => x.ObjectId.ToString() == response.RequestId);
                            if (stepToRemove != null)
                                request.ToSend.Remove(stepToRemove);

                            if (response.ResponseCode == ResponseCode.IndividualRetryAlreadyExists)
                            {
                                _graphGroupMembershipUpdaterLogger.LogInformationWithRunId(RunId, $"{response.RequestId} already exists");

                                _usersAlreadyExist.Add(new AzureADUser { ObjectId = Guid.Parse(response.RequestId) });
                            }
                            else if (response.ResponseCode == ResponseCode.GuestError)
                            {
                                _graphGroupMembershipUpdaterLogger.LogInformationWithRunId(RunId, $"{response.RequestId} was not added because it is a guest user and the destination does not allow guest users");

                                postResponse.ResponseCode = ResponseCode.GuestError;
                            }
                        }
                    }

                    // remove successful requests from the chunk, we only want to retry the ones that failed
                    var responseIds = responses.Select(x => Guid.Parse(x.RequestId)).ToList();
                    request.ToSend.RemoveAll(x => !responseIds.Contains(x.ObjectId));

                    if (request.ToSend.Any())
                    {
                        request.UpdateIdForRetry();
                        queue.Enqueue(request);
                        _graphGroupMembershipUpdaterLogger.LogInformationWithRunId(RunId, $"Requeued {request.Id}-{request.RetryCount}");
                    }
                }

                postResponse.SuccessCount = successfulRequests;
            }

            return postResponse;
        }

        private BatchRequestContentCollection CreatePOSTBatchRequestContent(ChunkOfUsers chunkOfUsers, Guid targetGroupId)
        {
            var batchRequestContent = new BatchRequestContentCollection(_graphServiceClient);
            foreach (var user in chunkOfUsers.ToSend)
            {
                var httpMethod = user.MembershipAction == MembershipAction.Add ? HttpMethod.Post : HttpMethod.Delete;

                var body = new JsonObject
                {
                    ["@odata.id"] = $"https://graph.microsoft.com/v1.0/directoryObjects/{user.ObjectId}"
                };

                var json = body.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

                var request = new HttpRequestMessage(httpMethod, $"https://graph.microsoft.com/v1.0/groups/{targetGroupId}/members/$ref")
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
                };

                batchRequestContent.AddBatchRequestStep(new BatchRequestStep(user.ObjectId.ToString(), request));
            }

            return batchRequestContent;
        }

        private async Task<(ResponseCode ResponseCode, int SuccessCount)> ProcessBatch(ConcurrentQueue<ChunkOfUsers> queue, List<ChunkOfUsers> toSend, MakeBulkRequest makeRequest, int threadNumber, Guid targetGroupId)
        {

            (ResponseCode ResponseCode, int SuccessCount) patchResponse = await ProcessPatchBatch(queue, toSend, makeRequest, threadNumber);
            (ResponseCode ResponseCode, int SuccessCount) postResponse = await ProcessPostBatch(queue, toSend, targetGroupId);

            return (patchResponse.ResponseCode == ResponseCode.Ok && postResponse.ResponseCode == ResponseCode.Ok
                    ? ResponseCode.Ok
                    : (patchResponse.ResponseCode == ResponseCode.GuestError || postResponse.ResponseCode == ResponseCode.GuestError) ?
                    ResponseCode.GuestError : ResponseCode.Error, patchResponse.SuccessCount + postResponse.SuccessCount);

        }

        private async Task<List<RetryResponse>> SendBatch(BatchRequestContentCollection tosend)
        {
            try
            {
                _graphGroupMembershipUpdaterLogger.LogInformationWithRunId(RunId, $"Sending requests {string.Join(",", tosend.BatchRequestSteps.Keys)}.");              
                var response = await _graphServiceClient.Batch.PostAsync(tosend);
                var responseStatusCodes = await response.GetResponsesStatusCodesAsync();
                var responses = await Task.WhenAll(responseStatusCodes.Select(async x => new KeyValuePair<string, HttpResponseMessage>(x.Key, await response.GetResponseByIdAsync(x.Key))));
                return await GetStepIdsToRetry(responses.ToDictionary(x => x.Key, x => x.Value), (Dictionary<string, BatchRequestStep>)tosend.BatchRequestSteps);
            }
            catch (ServiceException ex)
            {
                _graphGroupMembershipUpdaterLogger.LogErrorWithRunId(RunId, ex.GetBaseException().ToString(), ex);

                throw;
            }
        }

        private static TimeSpan CalculateThrottleWait(RetryConditionHeaderValue wait)
        {
            // we're much more likely to hit the write quota, so default to the 2 minute and 30 second wait
            // https://docs.microsoft.com/en-us/graph/throttling#pattern
            TimeSpan waitFor = TimeSpan.FromSeconds(150);
            if (wait == null) return waitFor;

            if (wait.Delta.HasValue) { waitFor = wait.Delta.Value; }
            if (wait.Date.HasValue) { waitFor = wait.Date.Value - DateTimeOffset.UtcNow; }
            return waitFor;
        }

        private static bool IsNotFoundError(string error)
        {
            var jsonDocument = JsonDocument.Parse(error);
            error = jsonDocument.RootElement.GetProperty("error").GetProperty("message").GetString();
            return error.Contains(_notFoundResponseError);
        }

        private static bool IsAlreadyExistsError(string error)
        {
            var jsonDocument = JsonDocument.Parse(error);
            error = jsonDocument.RootElement.GetProperty("error").GetProperty("message").GetString();
            return error.Contains(_alreadyExistsResponseError);
        }

        private async Task<List<RetryResponse>> GetStepIdsToRetry(
            Dictionary<string, HttpResponseMessage> responses, Dictionary<string, BatchRequestStep> requests)
        {
            var retryResponses = new List<RetryResponse>();
            bool beenThrottled = false;
            bool beenConcurrencyViolated = false;

            var writesUsed = _graphGroupMetricTracker.GetMetric(nameof(Metric.WritesUsed));
            var writeRequests = _graphGroupMetricTracker.GetMetric(nameof(Metric.WriteRequests));

            foreach (var kvp in responses)
            {
                //Ensure that the response messages get disposed of.
                using var response = kvp.Value;
                var status = response.StatusCode;
                var content = await response.Content.ReadAsStringAsync();

                var headers = response.Headers.ToDictionary(h => h.Key, h => h.Value);
                await _graphGroupMetricTracker.TrackMetricsAsync(headers, QueryType.Other, RunId, GraphOperationType.Write);

                _graphGroupMembershipUpdaterLogger.LogDebugWithRunId(RunId, $"Response - RequestId:{kvp.Key} - StatusCode:{status} - Content:{content}");

                writeRequests.TrackValue(1);

                // Note that the ones with empty bodies mean "this response is okay and we don't have to do anything about it."
                if (status == HttpStatusCode.BadRequest)
                {
                    if (IsAlreadyExistsError(content))
                    {
                        retryResponses.Add(new RetryResponse
                        {
                            RequestId = kvp.Key,
                            ResponseCode = ResponseCode.IndividualRetryAlreadyExists,
                            HttpStatusCode = HttpStatusCode.BadRequest
                        });
                    }
                    else
                    {
                        retryResponses.Add(new RetryResponse
                        {
                            RequestId = kvp.Key,
                            ResponseCode = ResponseCode.IndividualRetry,
                            HttpStatusCode = HttpStatusCode.BadRequest
                        });
                    }
                }
                else if (status == HttpStatusCode.NotFound && (content).Contains("does not exist or one of its queried reference-property objects are not present."))
                {
                    var requestStep = requests[kvp.Key];
                    
                    // For individual requests (DELETE/POST), we can use the requestId directly
                    // For batch PATCH requests, the requestId is a batch ID, not a user ID
                    if (requestStep.Request.Method == HttpMethod.Delete || requestStep.Request.Method == HttpMethod.Post)
                    {
                        // Use the requestId directly for individual requests to avoid parsing issues 
                        // where Graph may return group IDs in error messages instead of user IDs
                        var userId = requestStep.RequestId;

                        if (requestStep.Request.Method == HttpMethod.Delete)
                        {
                            _graphGroupMembershipUpdaterLogger.LogInformationWithRunId(RunId, $"Removing {userId} failed as this resource does not exists.");
                        }
                        else
                        {
                            _graphGroupMembershipUpdaterLogger.LogInformationWithRunId(RunId, $"Adding {userId} failed as this resource does not exists.");
                        }

                        _usersNotFound.Add(new AzureADUser { ObjectId = Guid.Parse(userId) });
                        
                        retryResponses.Add(new RetryResponse
                        {
                            RequestId = kvp.Key,
                            ResponseCode = ResponseCode.IndividualRetry,
                            AzureObjectId = userId,
                            HttpStatusCode = HttpStatusCode.NotFound
                        });
                    }
                    else
                    {
                        // For batch PATCH requests, we can't reliably determine the user ID from the response
                        // Just mark for individual retry - the specific user will be identified during retry
                        retryResponses.Add(new RetryResponse
                        {
                            RequestId = kvp.Key,
                            ResponseCode = ResponseCode.IndividualRetry,
                            HttpStatusCode = HttpStatusCode.NotFound
                        });
                    }
                }
                else if (status == HttpStatusCode.Conflict
                    && !string.IsNullOrWhiteSpace(content)
                    && content.Contains("Directory_ConcurrencyViolation", StringComparison.OrdinalIgnoreCase))
                {
                    if (!beenConcurrencyViolated)
                    {
                        var throttleWait = CalculateThrottleWait(response.Headers.RetryAfter);

                        _graphGroupMembershipUpdaterLogger.LogDebugWithRunId(RunId, $"Got 409 conflict due to concurrent updates. Waiting {throttleWait.TotalSeconds} seconds before retrying.");

                        await Task.Delay(throttleWait);
                        beenConcurrencyViolated = true;
                    }

                    _graphGroupMembershipUpdaterLogger.LogDebugWithRunId(RunId, $"Got 409 conflict due to concurrent updates. Retrying request {kvp.Key}.");

                    retryResponses.Add(new RetryResponse
                    {
                        RequestId = kvp.Key,
                        ResponseCode = ResponseCode.IndividualRetry,
                        HttpStatusCode = HttpStatusCode.Conflict
                    });
                }
                else if (_isOkay.Contains(status)) 
                { 
                    writesUsed.TrackValue(1);
                }
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
                        var gotThrottleInfo = response.Headers.TryGetValues(GraphResponseHeaders.ThrottleInformation, out var throttleInfo);
                        var gotThrottleScope = response.Headers.TryGetValues(GraphResponseHeaders.ThrottleScope, out var throttleScope);
                        _graphGroupMembershipUpdaterLogger.LogInformationWithRunId(
                            RunId,
                            string.Format("Got 429 throttled. Waiting {0} seconds. Delta: {1} Date: {2} Reason: {3} Scope: {4}",
                                throttleWait.TotalSeconds,
                                response.Headers.RetryAfter.Delta != null ? response.Headers.RetryAfter.Delta.ToString() : "(none)",
                                response.Headers.RetryAfter.Date != null ? response.Headers.RetryAfter.Date.ToString() : "(none)",
                                gotThrottleInfo ? string.Join(',', throttleInfo) : "(none)",
                                gotThrottleScope ? string.Join(',', throttleScope) : "(none)"));
                        await startThrottling;
                        beenThrottled = true;
                    }

                    // it's possible for only some requests in a batch to be throttled, so only retry the ones that were throttled.
                    retryResponses.Add(new RetryResponse
                    {
                        RequestId = kvp.Key,
                        ResponseCode = ResponseCode.IndividualRetry,
                        HttpStatusCode = HttpStatusCode.TooManyRequests
                    });
                }
                else if (status == HttpStatusCode.Forbidden && content.Contains("Guests users are not allowed to join"))
                {
                    retryResponses.Add(new RetryResponse
                    {
                        RequestId = kvp.Key,
                        HttpStatusCode = HttpStatusCode.Forbidden,
                        ResponseCode = ResponseCode.GuestError
                    });
                }
                else if (_shouldRetry.Contains(status))
                {
                    retryResponses.Add(new RetryResponse
                    {
                        RequestId = kvp.Key,
                        ResponseCode = ResponseCode.Ok,
                        HttpStatusCode = status
                    });
                }
                else
                {
                    _graphGroupMembershipUpdaterLogger.LogErrorWithRunId(RunId, $"Got an unexpected error from Graph, stopping all processing for current job: {status} {response.ReasonPhrase} {content}.");
                    retryResponses.Add(new RetryResponse
                    {
                        RequestId = kvp.Key,
                        ResponseCode = ResponseCode.Error
                    });
                }
            }

            return retryResponses;
        }


        private class ChunkOfUsers
        {
            public List<AzureADUser> ToSend { get; set; }

            public string Id { get; set; }


            public const int MaxBatchRetries = 5;
            public int RetryCount { get; set; }
            public bool SendAsPostRequest { get; set; }

            // basically, whenever a batch is retried, we append the thread number after a dash
            public bool ShouldRetry => RetryCount < MaxBatchRetries;
            public ChunkOfUsers UpdateIdForRetry()
            {
                RetryCount++;
                return this;
            }
        }

        private class RetryResponse
        {
            public string RequestId { get; set; }
            public ResponseCode ResponseCode { get; set; }
            public HttpStatusCode HttpStatusCode { get; set; }
            public string AzureObjectId { get; set; }
        }
    }
}
