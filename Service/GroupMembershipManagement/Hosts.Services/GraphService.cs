// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Models;
using Microsoft.Graph;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using Services.Contracts;
using Entities;
using Repositories.Contracts;

namespace Hosts.Services
{
    public class GraphService : IGraphService
    {
        private readonly int BATCH_REQUEST_LIMIT = 20;
        private readonly int FILTER_CONDITION_LIMIT = 10;

        private const string IdFieldName = "id";
        private const string PersonnelNumberFieldName = "onPremisesImmutableId";
        
        private readonly IGraphServiceClient _graphClient;       
        private readonly Dictionary<string, GraphProfileInformation> _cache;
        private readonly ILoggingRepository _loggingRepository = null;

        public GraphService(ILoggingRepository loggingRepository, IGraphServiceClient graphClient)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphClient = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
            _cache = new Dictionary<string, GraphProfileInformation>();
        }

        public async Task<IList<GraphProfileInformation>> GetAzureADObjectIds(IList<string> personnelNumbers)
        {
            if (personnelNumbers.Count == 0) return new List<GraphProfileInformation>();

            var correlationId = Guid.NewGuid();
            var baseProps = new Dictionary<string, string> { { "GraphServiceCorrelationId", correlationId.ToString() } };
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
            var _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{unprocessedPersonnelNumbers.Count} out of {personnelNumbers.Count} need to be retrieved from graph." });
            _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{_cache.Keys.Count} profiles exist in the cache." });
            var fields = string.Join(",", new[] { IdFieldName, PersonnelNumberFieldName });

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
                        var batchRequest = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/$batch")
                        {
                            Content = batchRequestContent
                        };
                        await _graphClient.AuthenticationProvider.AuthenticateRequestAsync(batchRequest);
                        var httpResponse = await _graphClient.HttpProvider.SendAsync(batchRequest);
                        var batchResponse = new BatchResponseContent(httpResponse);
                        var failures = 0;

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
                                        PersonnelNumber = user.OnPremisesImmutableId
                                    };
                                    profiles.Add(profile);
                                    _cache.Add(profile.PersonnelNumber, profile);
                                }
                            }
                            else
                            {
                                failures++;
                                var failureProps = new Dictionary<string, string>(baseProps)
                                {
                                    ["StatusCode"] = response.Value.StatusCode.ToString(),
                                    ["ReasonPhrase"] = response.Value.ReasonPhrase,
                                    ["RequestURI"] = batchRequestContent.BatchRequestSteps[response.Key].Request.RequestUri.ToString()
                                };                                    
                            }
                        }
                        batchTimer.Stop();
                        batchTimes.Add(batchTimer.Elapsed);
                        var batchMetrics = new Dictionary<string, double>
                        {
                            ["BatchNumber"] = batchCount,
                            ["TotalBatches"] = totalBatches,
                            ["ProcessingTime"] = batchTimer.Elapsed.TotalSeconds,
                            ["Failures"] = failures
                        };
                        var eventProps = new Dictionary<string, string>(baseProps);                                                
                        _ = _loggingRepository.LogMessageAsync(new LogMessage
                        {
                            Message = $"Graph Request: {batchCount} of {totalBatches}{Environment.NewLine}"
                                        + $"    Batch Time Elapsed: {batchTimer.ElapsedMilliseconds} ms{Environment.NewLine}"
                                        + $"    Total Time Elapsed: {jobTimer.Elapsed}{Environment.NewLine}"
                                        + $"    Total Profile Count: {profiles.Count}{Environment.NewLine}"
                                        + $"    Total Users Not Found: {(personnelNumbers.Count - pnQueue.Count) - profiles.Count}{Environment.NewLine}"
                                        + $"    Total Queue Remaining: {pnQueue.Count}{Environment.NewLine}"
                        });                                                
                        batchTimer.Reset();                   
                }
            }
            jobTimer.Stop();
            var jobMetrics = new Dictionary<string, double>
            {
                ["ProcessingTime"] = jobTimer.Elapsed.TotalSeconds,
                ["AvgTimePerJob"] = batchTimes?.Count > 0 ? batchTimes.Average(t => t.TotalSeconds) : 0,
                ["TotalBatches"] = totalBatches
            };
            var jobProps = new Dictionary<string, string>(baseProps);
                
            return profiles;            
        }
    }
}
