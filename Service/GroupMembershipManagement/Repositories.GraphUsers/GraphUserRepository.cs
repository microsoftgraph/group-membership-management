// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.Graph;
using Newtonsoft.Json;
using Polly;
using Polly.Wrap;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Repositories.GraphAzureADUsers
{
    public class GraphUserRepository : IGraphUserRepository
    {
        private const int BATCH_REQUEST_LIMIT = 20;
        private const int FILTER_CONDITION_LIMIT = 10;

        private const string IdFieldName = "id";
        private const string PersonnelNumberFieldName = "onPremisesImmutableId";
        private const string UserPrincipalNameFieldName = "userPrincipalName";

        private readonly Dictionary<string, GraphProfileInformation> _cache;
        private readonly IGraphServiceClient _graphClient;
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IGraphServiceAttemptsValue _maxGraphServiceAttempts;

        public GraphUserRepository(
                ILoggingRepository loggingRepository,
                IGraphServiceClient graphClient,
                IGraphServiceAttemptsValue maxGraphServiceAttempts
            )
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphClient = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
            _cache = new Dictionary<string, GraphProfileInformation>();
            _maxGraphServiceAttempts = maxGraphServiceAttempts;
        }

        public TimeSpan GetSleepDuration(int retryCount, DelegateResult<HttpResponseMessage> response, Context context)
        {
            var waitTime = response.Result.Headers.RetryAfter.Date.Value - DateTime.UtcNow;

            _ = _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Wait time set to {waitTime}",
                RunId = null
            });

            return waitTime;
        }

        public async Task<IList<GraphProfileInformation>> GetAzureADObjectIdsAsync(IList<string> personnelNumbers, Guid? runId)
        {
            if (personnelNumbers.Count == 0) return new List<GraphProfileInformation>();

            var graphMemberIdPolicy = GetRetryPolicy(runId);

            IList<string> unprocessedPersonnelNumbers = new List<string>();
            var profiles = new List<GraphProfileInformation>();
            if (_cache.Count > 0)
            {
                foreach (var personnelNumber in personnelNumbers)
                {
                    if (_cache.ContainsKey(personnelNumber))
                    {
                        profiles.Add(_cache[personnelNumber]);
                    }
                    else
                    {
                        unprocessedPersonnelNumbers.Add(personnelNumber);
                    }
                }
            }
            else
            {
                unprocessedPersonnelNumbers = personnelNumbers;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{unprocessedPersonnelNumbers.Count} out of {personnelNumbers.Count} need to be retrieved from graph.", RunId = runId });
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{_cache.Keys.Count} profiles exist in the cache.", RunId = runId });

            var fields = string.Join(",", new[] { IdFieldName, PersonnelNumberFieldName, UserPrincipalNameFieldName });

            // Graph currently limits batches to 10 requests per batch
            // Graph currently limits the number of conditions in a $filter expression to 20

            var requestsCreated = 0;
            var pnQueue = new Queue<string>(unprocessedPersonnelNumbers);
            var totalBatches = Math.Ceiling(unprocessedPersonnelNumbers.Count / BATCH_REQUEST_LIMIT / FILTER_CONDITION_LIMIT * 1d);
            var batchCount = 0;
            var jobTimer = new Stopwatch();
            var batchTimer = new Stopwatch();
            var batchTimes = new List<TimeSpan>();

            jobTimer.Start();

            while (pnQueue.Count > 0)
            {
                using (var batchRequestContent = new BatchRequestContent())
                {
                    int limit = pnQueue.Count >= FILTER_CONDITION_LIMIT ? 10 : pnQueue.Count;

                    // add request steps
                    while (batchRequestContent.BatchRequestSteps.Count < BATCH_REQUEST_LIMIT && pnQueue.Count != 0)
                    {
                        var requestPersonnelNumbers = new List<string>();
                        limit = pnQueue.Count >= FILTER_CONDITION_LIMIT ? 10 : pnQueue.Count;
                        for (var i = 0; i < limit; i++)
                        {
                            requestPersonnelNumbers.Add(pnQueue.Dequeue());
                        }

                        // build filter expression
                        var filter = $"(onPremisesImmutableId eq '{string.Join("' or onPremisesImmutableId eq '", requestPersonnelNumbers.ToArray())}')";
                        var requestMessage = _graphClient.Users.Request().Filter(filter).Select(fields).WithPerRequestAuthProvider().GetHttpRequestMessage();
                        requestsCreated++;

                        batchRequestContent.AddBatchRequestStep(new BatchRequestStep($"{requestsCreated}", requestMessage, null));
                    }

                    // every 20 requests send a batch or if there are no more requests to make
                    batchCount++;
                    batchTimer.Start();

                    HttpResponseMessage httpResponse = null;
                    await graphMemberIdPolicy.ExecuteAsync(async () =>
                    {
                        var batchRequest = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/$batch")
                        {
                            Content = batchRequestContent
                        };
                        await _graphClient.AuthenticationProvider.AuthenticateRequestAsync(batchRequest);

                        httpResponse = await _graphClient.HttpProvider.SendAsync(batchRequest);

                        await _loggingRepository.LogMessageAsync(new LogMessage
                        {
                            Message = $"Graph Response:"
                                    + $"\nStatusCode {httpResponse.StatusCode}",
                            RunId = runId
                        });

                        return httpResponse;
                    });

                    var batchResponse = new BatchResponseContent(httpResponse);

                    // process each request in the batch
                    foreach (var response in await batchResponse.GetResponsesAsync())
                    {
                        // request was successful
                        if (response.Value.IsSuccessStatusCode)
                        {
                            var content = await response.Value.Content.ReadAsStringAsync();
                            var oDataResponse = JsonConvert.DeserializeObject<ODataResponse<List<User>>>(content);

                            // process each user
                            foreach (var user in oDataResponse.Value)
                            {
                                var profile = new GraphProfileInformation
                                {
                                    Id = user.Id,
                                    PersonnelNumber = user.OnPremisesImmutableId,
                                    UserPrincipalName = user.UserPrincipalName
                                };

                                profiles.Add(profile);
                                _cache.Add(profile.PersonnelNumber, profile);
                            }
                        }
                        else
                        {
                            await _loggingRepository.LogMessageAsync(new LogMessage
                            {
                                Message = $"Graph Request failures:"
                                   + $"\nStatusCode {response.Value.StatusCode}"
                                   + $"\nReasonPhrase {response.Value.ReasonPhrase}"
                                   + $"\nRequestURI {batchRequestContent.BatchRequestSteps[response.Key].Request.RequestUri}",
                                RunId = runId
                            });
                        }
                    }

                    batchTimer.Stop();
                    batchTimes.Add(batchTimer.Elapsed);

                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"Graph Request: {batchCount} of {totalBatches}{Environment.NewLine}"
                                    + $"    Batch Time Elapsed: {batchTimer.ElapsedMilliseconds} ms{Environment.NewLine}"
                                    + $"    Total Time Elapsed: {jobTimer.Elapsed}{Environment.NewLine}"
                                    + $"    Total Profile Count: {profiles.Count}{Environment.NewLine}"
                                    + $"    Total Users Not Found: {(personnelNumbers.Count - pnQueue.Count) - profiles.Count}{Environment.NewLine}"
                                    + $"    Total Queue Remaining: {pnQueue.Count}{Environment.NewLine}",
                        RunId = runId
                    });

                    batchTimer.Reset();
                }
            }

            jobTimer.Stop();

            return profiles;
        }

        public async Task<List<GraphProfileInformation>> AddUsersAsync(List<User> users, Guid? runId)
        {
            var usersToProccess = new Queue<User>(users);
            var profiles = new List<GraphProfileInformation>();

            while (usersToProccess.Count > 0)
            {
                var batchRequestContent = CreateBatchRequestContent(usersToProccess);

                if (batchRequestContent.BatchRequestSteps.Count == 0)
                {
                    return profiles;
                }

                HttpResponseMessage httpResponse = await SendBatchRequestAsync(batchRequestContent, runId);
                var batchResponse = new BatchResponseContent(httpResponse);
                var allResponses = await batchResponse.GetResponsesAsync();

                var profileResponses = await ProcessIndividualResponsesAsync(allResponses, batchRequestContent);
                profiles.AddRange(profileResponses.Profiles);

                var newProfiles = await RetrySingleRequestsAsync(allResponses, new Queue<BatchRequestStep>(profileResponses.BatchSteps));
                profiles.AddRange(newProfiles);
            }

            return profiles;
        }

        private async Task<(List<GraphProfileInformation> Profiles, List<BatchRequestStep> BatchSteps)>
            ProcessIndividualResponsesAsync(Dictionary<string, HttpResponseMessage> responses, BatchRequestContent batchRequestContent)
        {
            var profiles = new List<GraphProfileInformation>();
            var stepsToRetry = new List<BatchRequestStep>();

            foreach (var response in responses)
            {
                if (response.Value.IsSuccessStatusCode)
                {
                    var profile = await ExtractProfileAsync(response.Value, response.Key);
                    profiles.Add(profile);
                }
                else if (response.Value.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    stepsToRetry.Add(batchRequestContent.BatchRequestSteps[response.Key]);
                }
                else
                {
                    await LogErrorAsync(response.Value, null);
                }
            }

            if (profiles.Count > 0)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Added {profiles.Count} new users.",
                    RunId = null
                });
            }

            if (stepsToRetry.Count > 0)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Too many requests. Requeueed {stepsToRetry.Count} requests.",
                    RunId = null
                });
            }

            return (profiles, stepsToRetry);
        }

        private async Task<List<GraphProfileInformation>> RetrySingleRequestsAsync(Dictionary<string, HttpResponseMessage> responses, Queue<BatchRequestStep> stepsToRetry)
        {
            var profiles = new List<GraphProfileInformation>();

            if (stepsToRetry.Count > 0)
            {
                var retryAfter = responses
                                        .Where(x => x.Value.Headers.RetryAfter != null && x.Value.Headers.RetryAfter.Delta.HasValue)
                                        .Select(x => x.Value.Headers.RetryAfter.Delta.Value)
                                        .Max();

                var waitTime = (int)retryAfter.TotalMilliseconds + 30000;

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Waiting for {waitTime / 1000} seconds to continue.",
                    RunId = null
                });

                await Task.Delay(waitTime);
            }

            while (stepsToRetry.Count > 0)
            {
                try
                {
                    var batchStep = stepsToRetry.Dequeue();
                    var singleResponse = await SendSingleRequestAsync(batchStep.Request, null);

                    if (singleResponse.IsSuccessStatusCode)
                    {
                        var profile = await ExtractProfileAsync(singleResponse, batchStep.RequestId);
                        profiles.Add(profile);

                        await _loggingRepository.LogMessageAsync(new LogMessage
                        {
                            Message = $"Added new user.",
                            RunId = null
                        });
                    }
                    else
                    {
                        await LogErrorAsync(singleResponse, null);
                    }
                }
                catch (Exception ex)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"Error sending single request:\n{ex.Message}",
                        RunId = null
                    });
                }
            }

            return profiles;
        }

        private BatchRequestContent CreateBatchRequestContent(Queue<User> usersToProccess)
        {
            var batchRequestContent = new BatchRequestContent();
            {
                while (batchRequestContent.BatchRequestSteps.Count < BATCH_REQUEST_LIMIT && usersToProccess.Count != 0)
                {
                    var user = usersToProccess.Dequeue();
                    var requestMessage = _graphClient.Users.Request().WithPerRequestAuthProvider().GetHttpRequestMessage();
                    requestMessage.Content = new StringContent(JsonConvert.SerializeObject(user), Encoding.UTF8, "application/json");
                    requestMessage.Method = HttpMethod.Post;
                    batchRequestContent.AddBatchRequestStep(new BatchRequestStep($"{user.OnPremisesImmutableId}", requestMessage, null));
                }
            }

            return batchRequestContent;
        }

        private async Task<GraphProfileInformation> ExtractProfileAsync(HttpResponseMessage response, string personnelNumber)
        {
            var content = await response.Content.ReadAsStringAsync();
            var user = JsonConvert.DeserializeObject<User>(content);
            var profile = new GraphProfileInformation
            {
                Id = user.Id,
                PersonnelNumber = personnelNumber,
                UserPrincipalName = user.UserPrincipalName
            };

            return profile;
        }

        private async Task<HttpResponseMessage> SendSingleRequestAsync(HttpRequestMessage request, Guid? runId)
        {
            var retryPolicy = GetRetryPolicy(runId);
            HttpResponseMessage httpResponse = await retryPolicy.ExecuteAsync(async () =>
            {
                return await _graphClient.HttpProvider.SendAsync(request);
            });

            return httpResponse;
        }

        private async Task<HttpResponseMessage> SendBatchRequestAsync(BatchRequestContent batchRequestContent, Guid? runId)
        {
            var retryPolicy = GetRetryPolicy(runId);
            HttpResponseMessage httpResponse = await retryPolicy.ExecuteAsync(async () =>
            {
                var batchRequest = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/$batch")
                {
                    Content = batchRequestContent
                };

                await _graphClient.AuthenticationProvider.AuthenticateRequestAsync(batchRequest);

                var response = await _graphClient.HttpProvider.SendAsync(batchRequest);

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Graph Response:"
                            + $"\nStatusCode {response.StatusCode}",
                    RunId = runId
                });

                return response;
            });

            return httpResponse;
        }

        private AsyncPolicyWrap<HttpResponseMessage> GetRetryPolicy(Guid? runId)
        {
            HttpStatusCode[] httpsStatusCodesWithRetryAfterHeader = {
                HttpStatusCode.TooManyRequests, // 429
                HttpStatusCode.ServiceUnavailable // 503
            };

            var retryAfterPolicy = Policy
                            .HandleResult<HttpResponseMessage>(result =>
                                httpsStatusCodesWithRetryAfterHeader.Contains(result.StatusCode) && result.Headers?.RetryAfter != null)
                            .WaitAndRetryAsync(
                                    _maxGraphServiceAttempts.MaxRetryAfterAttempts,
                                    sleepDurationProvider: GetSleepDuration,
                                    onRetryAsync: async (response, timeSpan, retryCount, context) =>
                                    {
                                        await _loggingRepository.LogMessageAsync(new LogMessage
                                        {
                                            Message = $"Throttled by Graph for the timespan: {timeSpan}. The retry count is {retryCount}.",
                                            RunId = runId
                                        });
                                    });

            HttpStatusCode[] httpStatusCodesWorthRetryingExponentially = {
                HttpStatusCode.InternalServerError, // 500
                HttpStatusCode.BadGateway, // 502
                HttpStatusCode.ServiceUnavailable, // 503
                HttpStatusCode.GatewayTimeout // 504
            };

            var exceptionHandlingPolicy = Policy
                            .Handle<HttpRequestException>()
                            .OrResult<HttpResponseMessage>(r => httpStatusCodesWorthRetryingExponentially.Contains(r.StatusCode))
                            .WaitAndRetryAsync(
                                _maxGraphServiceAttempts.MaxExceptionHandlingAttempts,
                                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                                onRetryAsync: async (timeSpan, retryCount, context) =>
                                {
                                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Exponential backoff {retryCount}.", RunId = runId });
                                }
                            );

            return retryAfterPolicy.WrapAsync(exceptionHandlingPolicy);
        }

        private async Task LogErrorAsync(HttpResponseMessage response, Guid? runId)
        {
            var content = await response.Content.ReadAsStringAsync();
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Graph Request failures:"
                   + $"\nStatusCode {response.StatusCode}"
                   + $"\nReasonPhrase {response.ReasonPhrase}"
                   + $"\nContent {content}",
                RunId = runId
            });
        }
    }
}
