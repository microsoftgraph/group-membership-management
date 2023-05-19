// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Models;
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
        private readonly GraphServiceClient _graphClient;
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IGraphServiceAttemptsValue _maxGraphServiceAttempts;

        public GraphUserRepository(
                ILoggingRepository loggingRepository,
                GraphServiceClient graphClient,
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

            var fields = new[] { IdFieldName, PersonnelNumberFieldName, UserPrincipalNameFieldName };

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
                using (var batchRequestContent = new BatchRequestContent(_graphClient))
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
                        var requestInformation = _graphClient.Users.ToGetRequestInformation(requestConfiguration =>
                        {
                            requestConfiguration.QueryParameters.Select = fields;
                            requestConfiguration.QueryParameters.Filter = filter;
                        });
                        requestsCreated++;

                        await batchRequestContent.AddBatchRequestStepAsync(requestInformation);
                    }

                    // every 20 requests send a batch or if there are no more requests to make
                    batchCount++;
                    batchTimer.Start();

                    var batchResponse = await _graphClient.Batch.PostAsync(batchRequestContent);

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

        public async Task<List<GraphProfileInformation>> AddUsersAsync(List<GraphUser> users, Guid? runId)
        {
            var usersToProcess = new Queue<GraphUser>(users);
            var profiles = new List<GraphProfileInformation>();

            while (usersToProcess.Count > 0)
            {
                var batchRequestContent = await CreateBatchRequestContentAsync(usersToProcess);

                if (batchRequestContent.BatchRequestSteps.Count == 0)
                {
                    return profiles;
                }

                var batchResponse = await SendBatchRequestAsync(batchRequestContent);
                var allResponses = await batchResponse.GetResponsesAsync();

                var profileResponses = await ProcessIndividualResponsesAsync(allResponses);
                profiles.AddRange(profileResponses.Profiles);

                var usersToRetry = users.Where(u => profileResponses.UserIdsToRetry.Contains(u.OnPremisesImmutableId));
                var newProfiles = await RetrySingleRequestsAsync(allResponses, new Queue<GraphUser>(usersToRetry));
                profiles.AddRange(newProfiles);
            }

            return profiles;
        }

        private async Task<(List<GraphProfileInformation> Profiles, List<string> UserIdsToRetry)>
            ProcessIndividualResponsesAsync(Dictionary<string, HttpResponseMessage> responses)
        {
            var profiles = new List<GraphProfileInformation>();
            var userIdsToRetry = new List<string>();

            foreach (var response in responses)
            {
                if (response.Value.IsSuccessStatusCode)
                {
                    var profile = await ExtractProfileAsync(response.Value, response.Key);
                    profiles.Add(profile);
                }
                else if (response.Value.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    userIdsToRetry.Add(response.Key);
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

            if (userIdsToRetry.Count > 0)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Too many requests. Requeueed {userIdsToRetry.Count} requests.",
                    RunId = null
                });
            }

            return (profiles, userIdsToRetry);
        }

        private async Task<List<GraphProfileInformation>> RetrySingleRequestsAsync(
            Dictionary<string, HttpResponseMessage> responses,
            Queue<GraphUser> usersToRetry)
        {
            var profiles = new List<GraphProfileInformation>();

            if (usersToRetry.Count > 0)
            {
                var retryAfter = responses
                                        .Where(x => x.Value.Headers.RetryAfter != null && x.Value.Headers.RetryAfter.Delta.HasValue)
                                        .Select(x => x.Value.Headers.RetryAfter.Delta.Value);

                var maxDelta = retryAfter.Any() ? retryAfter.Max() : TimeSpan.Zero;

                var waitTime = (int)maxDelta.TotalMilliseconds + 30000;

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Waiting for {waitTime / 1000} seconds to continue.",
                    RunId = null
                });

                await Task.Delay(waitTime);
            }

            while (usersToRetry.Count > 0)
            {
                try
                {
                    var user = usersToRetry.Dequeue();
                    var singleResponse = await SendSingleRequestAsync(user, null);

                    if (singleResponse.IsSuccessStatusCode)
                    {
                        var profile = await ExtractProfileAsync(singleResponse, user.OnPremisesImmutableId);
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

        private async Task<BatchRequestContent> CreateBatchRequestContentAsync(Queue<GraphUser> usersToProccess)
        {
            var batchRequestContent = new BatchRequestContent(_graphClient);

            while (batchRequestContent.BatchRequestSteps.Count < BATCH_REQUEST_LIMIT && usersToProccess.Count != 0)
            {
                var graphUser = usersToProccess.Dequeue();
                var user = MapUserDTOtoEntity(graphUser);
                var postRequestInformation = _graphClient.Users.ToPostRequestInformation(user);
                var requestMessage = await _graphClient.RequestAdapter.ConvertToNativeRequestAsync<HttpRequestMessage>(postRequestInformation);
                batchRequestContent.AddBatchRequestStep(new BatchRequestStep($"{user.OnPremisesImmutableId}", requestMessage, null));
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

        private async Task<HttpResponseMessage> SendSingleRequestAsync(GraphUser user, Guid? runId)
        {
            var retryPolicy = GetRetryPolicy(runId);
            HttpResponseMessage httpResponse = await retryPolicy.ExecuteAsync(async () =>
            {
                var nativeResponseHandler = new NativeResponseHandler();
                var postRequest = _graphClient.Users.ToPostRequestInformation(MapUserDTOtoEntity(user), config =>
                {
                    config.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                });

                await _graphClient.RequestAdapter.SendNoContentAsync(postRequest);
                return nativeResponseHandler.Value as HttpResponseMessage;
            });

            return httpResponse;
        }

        private async Task<BatchResponseContent> SendBatchRequestAsync(BatchRequestContent batchRequestContent)
        {
            return await _graphClient.Batch.PostAsync(batchRequestContent);
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

        private User MapUserDTOtoEntity(GraphUser user)
        {
            return new User
            {
                DisplayName = user.DisplayName,
                AccountEnabled = user.AccountEnabled,
                PasswordProfile = new PasswordProfile { Password = user.Password },
                MailNickname = user.MailNickname,
                UsageLocation = user.UsageLocation,
                UserPrincipalName = user.UserPrincipalName,
                OnPremisesImmutableId = user.OnPremisesImmutableId
            };
        }
    }
}
