// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.Graph;
using Newtonsoft.Json;
using Polly;
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

        public static TimeSpan GetSleepDuration(int retryCount, DelegateResult<HttpResponseMessage> response, Context context) => response.Result.Headers.RetryAfter.Date.Value - DateTime.UtcNow;

        public async Task<IList<GraphProfileInformation>> GetAzureADObjectIdsAsync(IList<string> personnelNumbers, Guid? runId)
        {
            if (personnelNumbers.Count == 0) return new List<GraphProfileInformation>();

            HttpStatusCode[] httpsStatusCodesWithRetryAfterHeader = {
                HttpStatusCode.TooManyRequests, // 429
                HttpStatusCode.ServiceUnavailable // 503
            };

            var retryAfterPolicy = Policy
                            .HandleResult<HttpResponseMessage>(result =>
                                httpsStatusCodesWithRetryAfterHeader.Contains(result.StatusCode) &&
                                result.Headers?.RetryAfter != null)
                            .WaitAndRetryAsync(
                                    _maxGraphServiceAttempts.MaxRetryAfterAttempts,
                                    sleepDurationProvider: GetSleepDuration,
                                    onRetryAsync: async (response, timeSpan, retryCount, context) =>
                                    {
                                        await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Throttled by Graph for the timespan: {timeSpan}. The retry count is {retryCount}.", RunId = runId });
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

            var graphMemberIdPolicy = retryAfterPolicy.WrapAsync(exceptionHandlingPolicy);

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
    }
}
