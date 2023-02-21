// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Graph;
using Microsoft.Graph.Core.Requests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Models;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using Repositories.GraphGroups;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class GraphGroupRepositoryTests
    {
        [TestMethod]
        public async Task AddUsersIgnoreNotFoundToGroup()
        {
            var graphServiceClient = new Mock<IGraphServiceClient>();
            var logger = new Mock<ILoggingRepository>();
            var telemetryConfiguration = new TelemetryConfiguration("instrumentationkey");
            var telemetryClient = new TelemetryClient(telemetryConfiguration);
            var batchRequest = new Mock<IBatchRequest>();
            var batch = new Mock<IBatchRequestBuilder>();

            batch.Setup(x => x.Request()).Returns(batchRequest.Object);
            graphServiceClient.Setup(x => x.Batch).Returns(batch.Object);

            var users = new List<AzureADUser>();
            for (var i = 0; i < 50; i++)
            {
                users.Add(new AzureADUser { ObjectId = Guid.NewGuid(), MembershipAction = MembershipAction.Add });
            }

            var targetGroup = new AzureADGroup { ObjectId = Guid.Empty };

            BatchResponseContent batchResponseContent = null;
            var graphGroupRepository = new GraphGroupRepository(graphServiceClient.Object, telemetryClient, logger.Object);
            var usersNotFound = 0;
            var usersNotFoundLimit = 5;

            var usersNotFoundIds = new List<string>();
            var allRequestedIds = new Dictionary<string, int>();

            batchRequest.Setup(x => x.PostAsync(It.IsAny<BatchRequestContent>()))
                .Callback<BatchRequestContent>(async (request) =>
                {
                    var requests = new Dictionary<string, string>();
                    foreach (var step in request.BatchRequestSteps)
                    {
                        var stepContent = await step.Value.Request.Content.ReadAsStringAsync();
                        var contentAsJObject = JObject.Parse(stepContent);
                        var userIds = contentAsJObject["members@odata.bind"].Values()
                                    .Select(x => x.Value<string>().Replace("https://graph.microsoft.com/v1.0/users/", string.Empty))
                                    .ToList();

                        userIds.ForEach(x =>
                        {
                            if (allRequestedIds.ContainsKey(x))
                                allRequestedIds[x]++;
                            else
                                allRequestedIds[x] = 1;
                        });

                        if (usersNotFound != usersNotFoundLimit && userIds.Count > 1)
                        {
                            var id = userIds.Skip(usersNotFound++).First();
                            var requestId = step.Value.RequestId;
                            requests.Add(requestId, id);

                            usersNotFoundIds.Add(id);
                        }
                    }

                    var content = GenerateNotFoundBatchResponse(requests);
                    batchResponseContent = GenerateBatchResponseContent(content);
                })
                .ReturnsAsync(() => batchResponseContent);


            var response = await graphGroupRepository.AddUsersToGroup(users, targetGroup);
            var userNotFoundRequestCount = allRequestedIds.Where(x => usersNotFoundIds.Contains(x.Key)).ToList();

            Assert.IsTrue(userNotFoundRequestCount.All(x => x.Value == 1));
        }

        [TestMethod]
        public async Task RemoveUsersIgnoreNotFoundFromGroup()
        {
            var userIdRegexPattern = new Regex("members/(?<userId>.*?)/");
            var graphServiceClient = new Mock<IGraphServiceClient>();
            var logger = new Mock<ILoggingRepository>();
            var telemetryConfiguration = new TelemetryConfiguration("instrumentationkey");
            var telemetryClient = new TelemetryClient(telemetryConfiguration);
            var batchRequest = new Mock<IBatchRequest>();
            var batch = new Mock<IBatchRequestBuilder>();

            batch.Setup(x => x.Request()).Returns(batchRequest.Object);
            graphServiceClient.Setup(x => x.Batch).Returns(batch.Object);

            var users = new List<AzureADUser>();
            for (var i = 0; i < 25; i++)
            {
                users.Add(new AzureADUser { ObjectId = Guid.NewGuid(), MembershipAction = MembershipAction.Remove });
            }

            var targetGroup = new AzureADGroup { ObjectId = Guid.Empty };

            BatchResponseContent batchResponseContent = null;
            var graphGroupRepository = new GraphGroupRepository(graphServiceClient.Object, telemetryClient, logger.Object);
            var usersNotFound = 0;
            var usersNotFoundLimit = 5;

            var usersNotFoundIds1 = new List<string>();
            var usersNotFoundIds2 = new List<string>();
            var allRequestedIds = new Dictionary<string, int>();

            batchRequest.Setup(x => x.PostAsync(It.IsAny<BatchRequestContent>()))
                .Callback<BatchRequestContent>((request) =>
                {
                    var requests = new Dictionary<string, string>();
                    foreach (var step in request.BatchRequestSteps)
                    {
                        var idMatch = userIdRegexPattern.Match(step.Value.Request.RequestUri.AbsolutePath);
                        if (idMatch.Success)
                        {
                            var userId = idMatch.Groups["userId"].Value;
                            if (allRequestedIds.ContainsKey(userId))
                                allRequestedIds[userId]++;
                            else
                                allRequestedIds[userId] = 1;

                            if (usersNotFound != usersNotFoundLimit)
                            {
                                var requestId = step.Value.RequestId;
                                // for DELETE the group id is returned
                                requests.Add(requestId, targetGroup.ObjectId.ToString());

                                usersNotFoundIds1.Add(userId);
                                usersNotFoundIds2.Add(step.Key);
                            }
                        }
                    }

                    CollectionAssert.AreEquivalent(usersNotFoundIds1, usersNotFoundIds2);

                    var content = GenerateNotFoundBatchResponse(requests);
                    batchResponseContent = GenerateBatchResponseContent(content);
                })
                .ReturnsAsync(() => batchResponseContent);


            var response = await graphGroupRepository.RemoveUsersFromGroup(users, targetGroup);
            var userNotFoundRequestCount = allRequestedIds.Where(x => usersNotFoundIds1.Contains(x.Key)).ToList();

            Assert.IsTrue(userNotFoundRequestCount.All(x => x.Value == 1));
        }

        private BatchResponseContent GenerateBatchResponseContent(string httpContent)
        {
            var httpResponse = new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(httpContent)
            };

            return new BatchResponseContent(httpResponse);
        }

        private string GenerateNotFoundBatchResponse(Dictionary<string, string> requests)
        {
            var responses = new List<string>();
            foreach (var request in requests)
            {
                var template = GetErrorResponseTemplate();
                var message = $"Resource '{request.Value}' does not exist or one of its queried reference-property objects are not present.";
                var response = string.Format(template, request.Key, 404, "Request_ResourceNotFound", message, DateTime.Now, "request-id", "request-id");
                responses.Add(response);
            }

            return $"{{\"responses\": [{string.Join(",", responses)}]}}";
        }

        private string GetErrorResponseTemplate()
        {
            var response = @"
            {{
                ""id"": ""{0}"",
                ""status"": {1},
                ""headers"":
                {{
                    ""Cache-Control"": ""no-cache"",
                    ""x-ms-resource-unit"": ""1"",
                    ""Content-Type"": ""application/json""
                }},
                ""body"":
                {{
                    ""error"":
                    {{
                        ""code"": ""{2}"",
                        ""message"": ""{3}"",
                        ""innerError"":
                        {{
                            ""date"": ""{4}"",
                            ""request-id"": ""{5}"",
                            ""client-request-id"": ""{6}""
                        }}
                    }}
                }}
            }}";

            return response;
        }
    }
}
