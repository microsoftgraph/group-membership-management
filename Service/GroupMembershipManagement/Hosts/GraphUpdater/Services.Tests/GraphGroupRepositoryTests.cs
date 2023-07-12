// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using Repositories.GraphGroups;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class GraphGroupRepositoryTests
    {
        private const string GRAPH_API_V1_BASE_URL = "https://graph.microsoft.com/v1.0";
        private Mock<IRequestAdapter> _requestAdapter;

        [TestInitialize]
        public void Setup()
        {
            _requestAdapter = new Mock<IRequestAdapter>();
            _requestAdapter.SetupProperty(x => x.BaseUrl).SetReturnsDefault(GRAPH_API_V1_BASE_URL);

            string requestUrl = null;
            HttpMethod requestMethod = null;
            _requestAdapter.Setup(x => x.ConvertToNativeRequestAsync<HttpRequestMessage>(It.IsAny<RequestInformation>(), It.IsAny<CancellationToken>()))
                .Callback<object, object>((r, t) =>
                {
                    var request = r as RequestInformation;
                    requestUrl = request.URI.ToString();
                    requestMethod = new HttpMethod(request.HttpMethod.ToString());
                })
               .ReturnsAsync(() => new HttpRequestMessage(HttpMethod.Get, requestUrl));
        }

        [TestMethod]
        public async Task AddUsersIgnoreNotFoundToGroup()
        {
            BatchResponseContent batchResponseContent = null;
            var usersNotFound = 0;
            var usersNotFoundLimit = 5;

            var usersNotFoundIds = new List<string>();
            var allRequestedIds = new Dictionary<string, int>();

            _requestAdapter.Setup(x => x.SendNoContentAsync(It.IsAny<RequestInformation>(),
                                   It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                                   It.IsAny<CancellationToken>()
                                   )
                            ).Callback<object, object, object>(
                            (request, errorMapping, cancellationToken) =>
                            {
                                var requestInformation = request as RequestInformation;
                                var responseHandler = requestInformation.RequestOptions.First(x => x.GetType() == typeof(ResponseHandlerOption)) as ResponseHandlerOption;
                                var nativeResponseHandler = responseHandler.ResponseHandler as NativeResponseHandler;

                                int index = 0;
                                int byteContent;
                                var buffer = new byte[requestInformation.Content.Length];

                                while ((byteContent = requestInformation.Content.ReadByte()) != -1)
                                {
                                    buffer[index++] = (byte)byteContent;
                                }

                                var stringContent = Encoding.UTF8.GetString(buffer);
                                var root = JObject.Parse(stringContent);
                                var requestsNode = root["requests"].ToString();
                                var batchRequest = JArray.Parse(requestsNode);

                                var requests = new Dictionary<string, string>();
                                var userIds = new List<string>();

                                foreach (var step in batchRequest)
                                {
                                    //PATCH requests have multiple userIds per step
                                    if (step["method"].Value<string>() == "PATCH")
                                    {
                                        userIds = step["body"]["members@odata.bind"]
                                                  .Values()
                                                  .Select(x => x.Value<string>()
                                                  .Replace("https://graph.microsoft.com/v1.0/users/", string.Empty))
                                                  .ToList();
                                    }
                                    else
                                    {
                                        //POST requests have a single userId per step
                                        var userId = step["body"]["@odata.id"]
                                                        .Value<string>()
                                                        .Replace("https://graph.microsoft.com/v1.0/directoryObjects/", string.Empty);

                                        userIds.Add(userId);
                                    }

                                    userIds.ForEach(x =>
                                    {
                                        if (allRequestedIds.ContainsKey(x))
                                            allRequestedIds[x]++;
                                        else
                                            allRequestedIds[x] = 1;
                                    });

                                    if (usersNotFound != usersNotFoundLimit && userIds.Count > 1)
                                    {
                                        var id = userIds.Skip(usersNotFound++).FirstOrDefault();
                                        if (id != null)
                                        {
                                            var requestId = step["id"].Value<string>();
                                            requests.Add(requestId, id);

                                            usersNotFoundIds.Add(id);
                                        }
                                    }
                                }

                                var content = GenerateNotFoundBatchResponse(requests);
                                batchResponseContent = GenerateBatchResponseContent(content);
                                nativeResponseHandler.Value = GetHttpResponseMessage(content);
                            });

            var graphServiceClient = new Mock<GraphServiceClient>(_requestAdapter.Object, GRAPH_API_V1_BASE_URL);
            var logger = new Mock<ILoggingRepository>();
            var telemetryConfiguration = new TelemetryConfiguration("instrumentationkey");
            var telemetryClient = new TelemetryClient(telemetryConfiguration);

            var users = new List<AzureADUser>();
            for (var i = 0; i < 50; i++)
            {
                users.Add(new AzureADUser { ObjectId = Guid.NewGuid(), MembershipAction = MembershipAction.Add });
            }

            var targetGroup = new AzureADGroup { ObjectId = Guid.Empty };

            var graphGroupRepository = new GraphGroupRepository(graphServiceClient.Object, telemetryClient, logger.Object);

            var response = await graphGroupRepository.AddUsersToGroup(users, targetGroup);
            var userNotFoundRequestCount = allRequestedIds.Where(x => usersNotFoundIds.Contains(x.Key)).ToList();

            Assert.IsTrue(userNotFoundRequestCount.All(x => x.Value == 1));
        }

        [TestMethod]
        public async Task RemoveUsersIgnoreNotFoundFromGroup()
        {
            var userIdRegexPattern = new Regex("members/(?<userId>.*?)/");
            var logger = new Mock<ILoggingRepository>();
            var telemetryConfiguration = new TelemetryConfiguration("instrumentationkey");
            var telemetryClient = new TelemetryClient(telemetryConfiguration);

            var users = new List<AzureADUser>();
            for (var i = 0; i < 25; i++)
            {
                users.Add(new AzureADUser { ObjectId = Guid.NewGuid(), MembershipAction = MembershipAction.Remove });
            }

            var targetGroup = new AzureADGroup { ObjectId = Guid.NewGuid() };

            BatchResponseContent batchResponseContent = null;
            var usersNotFound = 0;
            var usersNotFoundLimit = 5;

            var usersNotFoundIds1 = new List<string>();
            var usersNotFoundIds2 = new List<string>();
            var allRequestedIds = new Dictionary<string, int>();

            _requestAdapter.Setup(x => x.SendNoContentAsync(It.IsAny<RequestInformation>(),
                                   It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                                   It.IsAny<CancellationToken>()
                                   )
                            ).Callback<object, object, object>(
                            (request, errorMapping, cancellationToken) =>
                            {
                                var requestInformation = request as RequestInformation;
                                var responseHandler = requestInformation.RequestOptions.First(x => x.GetType() == typeof(ResponseHandlerOption)) as ResponseHandlerOption;
                                var nativeResponseHandler = responseHandler.ResponseHandler as NativeResponseHandler;

                                int index = 0;
                                int byteContent;
                                var buffer = new byte[requestInformation.Content.Length];

                                while ((byteContent = requestInformation.Content.ReadByte()) != -1)
                                {
                                    buffer[index++] = (byte)byteContent;
                                }

                                var stringContent = Encoding.UTF8.GetString(buffer);
                                var root = JObject.Parse(stringContent);
                                var requestsNode = root["requests"].ToString();
                                var batchRequest = JArray.Parse(requestsNode);

                                var steps = batchRequest.Select(jtoken => new
                                {
                                    Id = jtoken["id"].Value<string>(),
                                    Url = jtoken["url"].Value<string>(),
                                }).ToList();

                                var requests = new Dictionary<string, string>();
                                foreach (var step in steps)
                                {
                                    var idMatch = userIdRegexPattern.Match(step.Url);
                                    if (idMatch.Success)
                                    {
                                        var userId = idMatch.Groups["userId"].Value;
                                        if (allRequestedIds.ContainsKey(userId))
                                            allRequestedIds[userId]++;
                                        else
                                            allRequestedIds[userId] = 1;

                                        if (usersNotFound != usersNotFoundLimit)
                                        {
                                            var requestId = step.Id;
                                            // for DELETE the group id is returned
                                            requests.Add(requestId, targetGroup.ObjectId.ToString());

                                            usersNotFoundIds1.Add(userId);
                                            usersNotFoundIds2.Add(step.Id);

                                            usersNotFound++;
                                        }
                                    }
                                }

                                CollectionAssert.AreEquivalent(usersNotFoundIds1, usersNotFoundIds2);

                                var content = GenerateNotFoundBatchResponse(requests);
                                batchResponseContent = GenerateBatchResponseContent(content);
                                nativeResponseHandler.Value = GetHttpResponseMessage(content);
                            });

            var graphServiceClient = new Mock<GraphServiceClient>(_requestAdapter.Object, GRAPH_API_V1_BASE_URL);
            var graphGroupRepository = new GraphGroupRepository(graphServiceClient.Object, telemetryClient, logger.Object);
            var response = await graphGroupRepository.RemoveUsersFromGroup(users, targetGroup);
            var userNotFoundRequestCount = allRequestedIds.Where(x => usersNotFoundIds1.Contains(x.Key)).ToList();

            Assert.IsTrue(userNotFoundRequestCount.All(x => x.Value == 1));
        }

        private HttpResponseMessage GetHttpResponseMessage(string httpContent)
        {
            var httpResponse = new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(httpContent)
            };

            return httpResponse;
        }

        private BatchResponseContent GenerateBatchResponseContent(string httpContent)
        {
            return new BatchResponseContent(GetHttpResponseMessage(httpContent));
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
