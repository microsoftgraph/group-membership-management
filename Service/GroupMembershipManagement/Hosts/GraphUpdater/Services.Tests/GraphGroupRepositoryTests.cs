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

            var usersNotFoundIds = new List<string>();
            var allRequestedIds = new Dictionary<string, int>();

            var users = new List<AzureADUser>();
            for (var i = 0; i < 45; i++)
            {
                users.Add(new AzureADUser { ObjectId = Guid.NewGuid(), MembershipAction = MembershipAction.Add });
            }

            // batch 1 has two users not found
            usersNotFoundIds.Add(users[10].ObjectId.ToString()); // this is processed as PATCH
            usersNotFoundIds.Add(users[15].ObjectId.ToString()); // this is processed as POST

            //batch 2 has two user not found
            usersNotFoundIds.Add(users[25].ObjectId.ToString()); // this is processed as PATCH
            usersNotFoundIds.Add(users[35].ObjectId.ToString()); // this is processed as POST

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
                                var individualResponsesForPOSTRequests = new List<string>();

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

                                        var userNotFound = userIds.Intersect(usersNotFoundIds).FirstOrDefault();
                                        if (userNotFound != null)
                                        {
                                            requests.Add(step["id"].Value<string>(), userNotFound);
                                            var content = GenerateNotFoundBatchResponse(requests);
                                            batchResponseContent = GenerateBatchResponseContent(content);
                                            nativeResponseHandler.Value = GetHttpResponseMessage(content);
                                        }
                                    }
                                    else
                                    {
                                        //POST requests have a single userId per step
                                        var userId = step["body"]["@odata.id"]
                                                        .Value<string>()
                                                        .Replace("https://graph.microsoft.com/v1.0/directoryObjects/", string.Empty);

                                        if (usersNotFoundIds.Contains(userId))
                                        {
                                            var id = step["id"].Value<string>();
                                            var message = $"Resource '{id}' does not exist or one of its queried reference-property objects are not present.";
                                            individualResponsesForPOSTRequests.Add(GenerateNotFoundIndividualResponse(id, message));
                                        }
                                        else
                                        {
                                            individualResponsesForPOSTRequests.Add(GenerateSuccessIndividualResponse(step["id"].Value<string>()));
                                        }
                                    }
                                }

                                // set the response for the POST requests
                                if (individualResponsesForPOSTRequests.Any())
                                {
                                    var content = $"{{\"responses\": [{string.Join(",", individualResponsesForPOSTRequests)}]}}";
                                    batchResponseContent = GenerateBatchResponseContent(content);
                                    nativeResponseHandler.Value = GetHttpResponseMessage(content);
                                }
                            });

            var graphServiceClient = new Mock<GraphServiceClient>(_requestAdapter.Object, GRAPH_API_V1_BASE_URL);
            var logger = new Mock<ILoggingRepository>();
            var telemetryConfiguration = new TelemetryConfiguration("instrumentationkey");
            var telemetryClient = new TelemetryClient(telemetryConfiguration);
            var targetGroup = new AzureADGroup { ObjectId = Guid.Empty };
            var graphGroupRepository = new GraphGroupRepository(graphServiceClient.Object, telemetryClient, logger.Object);
            var response = await graphGroupRepository.AddUsersToGroup(users, targetGroup);

            foreach (var userId in usersNotFoundIds)
            {
                var message = $"Adding {userId} failed as this resource does not exists.";
                logger.Verify(x => x.LogMessageAsync(
                                   It.Is<LogMessage>(x => x.Message == message),
                                   It.IsAny<VerbosityLevel>(),
                                   It.IsAny<string>(),
                                   It.IsAny<string>()), Times.Exactly(1));
            }

            Assert.AreEqual(usersNotFoundIds.Count, response.UsersNotFound.Count);
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
            var usersNotFoundIds = new List<string>
            {
                users[10].ObjectId.ToString(),
                users[23].ObjectId.ToString(),
            };

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

                                var individualResponsesForPOSTRequests = new List<string>();
                                foreach (var step in steps)
                                {
                                    var idMatch = userIdRegexPattern.Match(step.Url);
                                    if (idMatch.Success)
                                    {
                                        var userId = idMatch.Groups["userId"].Value;
                                        if (usersNotFoundIds.Contains(userId))
                                        {
                                            var message = $"Resource '{userId}' does not exist or one of its queried reference-property objects are not present.";
                                            individualResponsesForPOSTRequests.Add(GenerateNotFoundIndividualResponse(step.Id, message));
                                        }
                                        else
                                        {
                                            individualResponsesForPOSTRequests.Add(GenerateSuccessIndividualResponse(step.Id));
                                        }
                                    }
                                }

                                var content = $"{{\"responses\": [{string.Join(",", individualResponsesForPOSTRequests)}]}}";
                                batchResponseContent = GenerateBatchResponseContent(content);
                                nativeResponseHandler.Value = GetHttpResponseMessage(content);
                            });

            var graphServiceClient = new Mock<GraphServiceClient>(_requestAdapter.Object, GRAPH_API_V1_BASE_URL);
            var graphGroupRepository = new GraphGroupRepository(graphServiceClient.Object, telemetryClient, logger.Object);
            var response = await graphGroupRepository.RemoveUsersFromGroup(users, targetGroup);

            foreach (var userId in usersNotFoundIds)
            {
                var message = $"Removing {userId} failed as this resource does not exists.";
                logger.Verify(x => x.LogMessageAsync(
                                   It.Is<LogMessage>(x => x.Message == message),
                                   It.IsAny<VerbosityLevel>(),
                                   It.IsAny<string>(),
                                   It.IsAny<string>()), Times.Exactly(1));
            }

            Assert.AreEqual(usersNotFoundIds.Count, response.UsersNotFound.Count);
        }

        [TestMethod]
        // number of users, number of users that already exist per batch request
        [DataRow(1, 1)]
        [DataRow(30, 2)]
        [DataRow(200, 5)]
        public async Task HandleUserAlreadyExistsResponse(int numberOfUsers, int numberOfUsersPerPageThatAlreadyExist)
        {
            BatchResponseContent batchResponseContent = null;
            var usersThatAlreadyExist = new List<string>();
            var userAlreadyExistsMessage = "One or more added object references already exist for the following modified properties: 'members'.";

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

                    var individualResponsesForPOSTRequests = new List<string>();

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

                            if (userIds.Any(x => usersThatAlreadyExist.Contains(x)))
                            {
                                requests.Add(step["id"].Value<string>(), usersThatAlreadyExist.First());
                                var content = GenerateBadRequestBatchResponse(requests, userAlreadyExistsMessage);
                                batchResponseContent = GenerateBatchResponseContent(content);
                                nativeResponseHandler.Value = GetHttpResponseMessage(content);
                            }
                        }
                        else
                        {
                            //POST requests have a single userId per step
                            var userId = step["body"]["@odata.id"]
                                            .Value<string>()
                                            .Replace("https://graph.microsoft.com/v1.0/directoryObjects/", string.Empty);

                            if (usersThatAlreadyExist.Contains(userId))
                            {
                                individualResponsesForPOSTRequests.Add(GenerateBadRequestIndividualResponse(step["id"].Value<string>(), userAlreadyExistsMessage));
                            }
                            else
                            {
                                individualResponsesForPOSTRequests.Add(GenerateSuccessIndividualResponse(step["id"].Value<string>()));
                            }
                        }
                    }

                    // set the response for the POST requests
                    if (individualResponsesForPOSTRequests.Any())
                    {
                        var content = $"{{\"responses\": [{string.Join(",", individualResponsesForPOSTRequests)}]}}";
                        batchResponseContent = GenerateBatchResponseContent(content);
                        nativeResponseHandler.Value = GetHttpResponseMessage(content);
                    }
                });

            var graphServiceClient = new Mock<GraphServiceClient>(_requestAdapter.Object, GRAPH_API_V1_BASE_URL);
            var logger = new Mock<ILoggingRepository>();
            var telemetryConfiguration = new TelemetryConfiguration("instrumentationkey");
            var telemetryClient = new TelemetryClient(telemetryConfiguration);
            var graphGroupRepository = new GraphGroupRepository(graphServiceClient.Object, telemetryClient, logger.Object);

            var usersToAdd = new List<AzureADUser>();
            Enumerable.Range(0, numberOfUsers)
                      .ToList()
                      .ForEach(x => usersToAdd.Add(new AzureADUser
                      {
                          MembershipAction = MembershipAction.Add,
                          ObjectId = Guid.NewGuid(),
                      }));

            foreach (var pageOfUsers in usersToAdd.Chunk(20))
            {
                for (int i = 0; i < numberOfUsersPerPageThatAlreadyExist && i < pageOfUsers.Count(); i++)
                {
                    usersThatAlreadyExist.Add(pageOfUsers[i].ObjectId.ToString());
                }
            }

            var response = await graphGroupRepository.AddUsersToGroup(usersToAdd, new AzureADGroup { ObjectId = Guid.NewGuid() });

            logger.Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(x => x.Message.EndsWith("already exists")),
                                    It.IsAny<VerbosityLevel>(),
                                    It.IsAny<string>(),
                                    It.IsAny<string>()), Times.Exactly(usersThatAlreadyExist.Count));

            Assert.AreEqual(usersThatAlreadyExist.Count, response.UsersAlreadyExist.Count);
        }

        [TestMethod]
        // number of users, number of guest users per batch request
        [DataRow(1, 1)]
        [DataRow(2, 1)]
        [DataRow(10, 2)]
        [DataRow(30, 2)]
        [DataRow(200, 5)]
        public async Task HandleGuestUserResponse(int numberOfUsers, int numberOfGuestUsersPerPage)
        {
            BatchResponseContent batchResponseContent = null;
            var guestUsers = new List<string>();
            var guestUserErrorMessage = "Guests users are not allowed to join this Unified Group due to policy setting.";

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

                    var individualResponsesForPOSTRequests = new List<string>();

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

                            if (userIds.Any(x => guestUsers.Contains(x)))
                            {
                                requests.Add(step["id"].Value<string>(), guestUsers.First());
                                var content = GenerateForbiddenBatchResponse(requests, guestUserErrorMessage);
                                batchResponseContent = GenerateBatchResponseContent(content);
                                nativeResponseHandler.Value = GetHttpResponseMessage(content);
                            }
                        }
                        else
                        {
                            //POST requests have a single userId per step
                            var userId = step["body"]["@odata.id"]
                                            .Value<string>()
                                            .Replace("https://graph.microsoft.com/v1.0/directoryObjects/", string.Empty);

                            if (guestUsers.Contains(userId))
                            {
                                individualResponsesForPOSTRequests.Add(GenerateForbiddenIndividualResponse(step["id"].Value<string>(), guestUserErrorMessage));
                            }
                            else
                            {
                                individualResponsesForPOSTRequests.Add(GenerateSuccessIndividualResponse(step["id"].Value<string>()));
                            }
                        }
                    }

                    // set the response for the POST requests
                    if (individualResponsesForPOSTRequests.Any())
                    {
                        var content = $"{{\"responses\": [{string.Join(",", individualResponsesForPOSTRequests)}]}}";
                        batchResponseContent = GenerateBatchResponseContent(content);
                        nativeResponseHandler.Value = GetHttpResponseMessage(content);
                    }
                });

            var graphServiceClient = new Mock<GraphServiceClient>(_requestAdapter.Object, GRAPH_API_V1_BASE_URL);
            var logger = new Mock<ILoggingRepository>();
            var telemetryConfiguration = new TelemetryConfiguration("instrumentationkey");
            var telemetryClient = new TelemetryClient(telemetryConfiguration);
            var graphGroupRepository = new GraphGroupRepository(graphServiceClient.Object, telemetryClient, logger.Object);
            var usersToAdd = new List<AzureADUser>();

            Enumerable.Range(0, numberOfUsers)
                      .ToList()
                      .ForEach(x => usersToAdd.Add(new AzureADUser
                      {
                          MembershipAction = MembershipAction.Add,
                          ObjectId = Guid.NewGuid(),
                      }));

            foreach (var pageOfUsers in usersToAdd.Chunk(20))
            {
                for (int i = 0; i < numberOfGuestUsersPerPage && i < pageOfUsers.Count(); i++)
                {
                    guestUsers.Add(pageOfUsers[i].ObjectId.ToString());
                }
            }

            var response = await graphGroupRepository.AddUsersToGroup(usersToAdd, new AzureADGroup { ObjectId = Guid.NewGuid() });

            foreach (var guestUser in guestUsers)
            {
                var message = $"{guestUser} was not added because it is a guest user and the destination does not allow guest users";
                logger.Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(x => x.Message == message),
                                    It.IsAny<VerbosityLevel>(),
                                    It.IsAny<string>(),
                                    It.IsAny<string>()), Times.Exactly(1));
            }
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
                var message = $"Resource '{request.Value}' does not exist or one of its queried reference-property objects are not present.";
                var response = GenerateNotFoundIndividualResponse(request.Key, message);
                responses.Add(response);
            }

            return $"{{\"responses\": [{string.Join(",", responses)}]}}";
        }

        private string GenerateBadRequestBatchResponse(Dictionary<string, string> requests, string message)
        {
            var responses = new List<string>();
            foreach (var request in requests)
            {
                var response = GenerateBadRequestIndividualResponse(request.Key, message);
                responses.Add(response);
            }

            return $"{{\"responses\": [{string.Join(",", responses)}]}}";
        }

        private string GenerateForbiddenBatchResponse(Dictionary<string, string> requests, string message)
        {
            var responses = new List<string>();
            foreach (var request in requests)
            {
                var response = GenerateForbiddenIndividualResponse(request.Key, message);
                responses.Add(response);
            }

            return $"{{\"responses\": [{string.Join(",", responses)}]}}";
        }

        private string GenerateSuccessIndividualResponse(string requestId)
        {
            return $"{{\"id\":\"{requestId}\",\"status\":204,\"headers\":{{\"Cache-Control\":\"no-cache\",\"x-ms-resource-unit\":\"1\"}},\"body\":null}}";
        }

        private string GenerateBadRequestIndividualResponse(string requestId, string message)
        {
            return GenerateFailedIndividualResponse(requestId, "400", "Request_BadRequest", message);
        }

        private string GenerateNotFoundIndividualResponse(string requestId, string message)
        {
            return GenerateFailedIndividualResponse(requestId, "404", "Request_ResourceNotFound", message);
        }

        private string GenerateForbiddenIndividualResponse(string requestId, string message)
        {
            return GenerateFailedIndividualResponse(requestId, "403", "Authorization_RequestDenied", message);
        }

        private string GenerateFailedIndividualResponse(string requestId, string statusCode, string errorCode, string message)
        {
            var template = GetErrorResponseTemplate();
            var response = string.Format(template, requestId, statusCode, errorCode, message, DateTime.Now, "request-id", "request-id");
            return response;
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

