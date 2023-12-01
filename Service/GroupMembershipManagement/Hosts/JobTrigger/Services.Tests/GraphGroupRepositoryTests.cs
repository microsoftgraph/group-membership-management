// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using Repositories.GraphGroups;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class GraphGroupRepositoryTests
    {
        private const string GRAPH_API_V1_BASE_URL = "https://graph.microsoft.com/v1.0";

        private TelemetryClient _telemetryClient = new TelemetryClient(new TelemetryConfiguration("instrumentationkey"));
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<GraphServiceClient> _graphServiceClient;
        private GraphGroupRepository _graphGroupRepository;
        private Mock<IRequestAdapter> _requestAdapter;
        private Mock<IResponseHandler> _responseHandler;

        [TestInitialize]
        public void InitializeTest()
        {
            _loggingRepository = new Mock<ILoggingRepository>();

            _requestAdapter = new Mock<IRequestAdapter>();
            _responseHandler = new Mock<IResponseHandler>();

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

            _graphServiceClient = new Mock<GraphServiceClient>(_requestAdapter.Object, GRAPH_API_V1_BASE_URL);
            _graphGroupRepository = new GraphGroupRepository(_graphServiceClient.Object, _telemetryClient, _loggingRepository.Object);
        }

        [TestMethod]
        [DynamicData(nameof(GetEndPointsData), DynamicDataSourceType.Method)]
        public async Task TestGetGroupEndpointsAsync(Guid groupId, bool isMailEnabled, bool isSecurityEnabled, string[] groupTypes, string[] providers)
        {
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

                        var requests = batchRequest.Select(jtoken => new
                        {
                            id = jtoken["id"].Value<string>(),
                            url = jtoken["url"].Value<string>(),
                        }).ToList();

                        var requestIds = new Dictionary<string, string>
                        {
                            { "outlook", requests[0].id },
                            { "sharepoint", requests[1].id }
                        };

                        var content = GetSampleResponse(isMailEnabled, isSecurityEnabled, groupTypes, requestIds);
                        nativeResponseHandler.Value = content;

                    });

            _requestAdapter.Setup(x => x.SendAsync(
                                                    It.IsAny<RequestInformation>(),
                                                    It.IsAny<ParsableFactory<EndpointCollectionResponse>>(),
                                                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                                                    It.IsAny<CancellationToken>()
                                                  )
                                 )
                            .ReturnsAsync(() =>
                            {
                                var collectionReponse = new EndpointCollectionResponse() { Value = new List<Endpoint>() };
                                foreach (var provider in providers)
                                {
                                    collectionReponse.Value.Add(new Endpoint() { ProviderName = provider });
                                }

                                return collectionReponse;
                            });

            _graphServiceClient = new Mock<GraphServiceClient>(_requestAdapter.Object, GRAPH_API_V1_BASE_URL);
            _graphGroupRepository = new GraphGroupRepository(_graphServiceClient.Object, _telemetryClient, _loggingRepository.Object);

            var endPoints = await _graphGroupRepository.GetGroupEndpointsAsync(groupId);

            Assert.IsTrue(endPoints.Contains("Outlook") || endPoints.Contains("SecurityGroup"));

            if (providers.Length > 0)
            {
                Assert.IsTrue(providers.All(x => endPoints.Contains(x)));
            }
        }

        [TestMethod]
        public async Task TestGroupExistsSucceedsAsync()
        {
            var groupId = Guid.NewGuid();
            var chaosHandlerOption = new ChaosHandlerOption
            {
                PlannedChaosFactory = (request) =>
                {
                    var content = GetGroupRawResponse(groupId.ToString());
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    response.Content = new StringContent(content, Encoding.UTF8, "application/json");
                    return response;
                }
            };
            var graphServiceClient = CreateCustomGraphServiceClient(chaosHandlerOption);
            _graphGroupRepository = new GraphGroupRepository(graphServiceClient, _telemetryClient, _loggingRepository.Object);
            var groupExists = await _graphGroupRepository.GroupExists(Guid.NewGuid());

            Assert.IsTrue(groupExists);
        }

        [TestMethod]
        public async Task TestGroupExistsSucceedsAfterRetriesAsync()
        {
            var groupId = Guid.NewGuid();
            var currentRetry = 0;
            var chaosHandlerOption = new ChaosHandlerOption
            {
                PlannedChaosFactory = (request) =>
                {
                    HttpResponseMessage response = null;

                    if (currentRetry < 2)
                    {
                        response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                    }
                    else if (currentRetry < 3)
                    {
                        response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                    }
                    else
                    {
                        var content = GetGroupRawResponse(groupId.ToString());
                        response = new HttpResponseMessage(HttpStatusCode.OK);
                        response.Content = new StringContent(content, Encoding.UTF8, "application/json");
                    }

                    currentRetry++;

                    return response;
                }
            };
            var graphServiceClient = CreateCustomGraphServiceClient(chaosHandlerOption);
            _graphGroupRepository = new GraphGroupRepository(graphServiceClient, _telemetryClient, _loggingRepository.Object);
            var groupExists = await _graphGroupRepository.GroupExists(Guid.NewGuid());

            Assert.IsTrue(groupExists);
        }

        [TestMethod]
        public async Task TestGroupExistsSucceedsFailsRetriesAsync()
        {
            var chaosHandlerOption = new ChaosHandlerOption
            {
                PlannedChaosFactory = (request) =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                    return response;
                }
            };
            var graphServiceClient = CreateCustomGraphServiceClient(chaosHandlerOption);
            _graphGroupRepository = new GraphGroupRepository(graphServiceClient, _telemetryClient, _loggingRepository.Object);
            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _graphGroupRepository.GroupExists(Guid.NewGuid()));

            Assert.IsTrue(exception.Message.Contains("Too many retries performed"));
        }

        [TestMethod]
        public async Task TestGroupExistsNotFoundAsync()
        {
            var chaosHandlerOption = new ChaosHandlerOption
            {
                PlannedChaosFactory = (request) => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
            var graphServiceClient = CreateCustomGraphServiceClient(chaosHandlerOption);
            _graphGroupRepository = new GraphGroupRepository(graphServiceClient, _telemetryClient, _loggingRepository.Object);
            var groupExists = await _graphGroupRepository.GroupExists(Guid.NewGuid());

            Assert.IsFalse(groupExists);
        }


        private GraphServiceClient CreateCustomGraphServiceClient(ChaosHandlerOption chaosHandlerOption)
        {
            var chaosHandler = new ChaosHandler(chaosHandlerOption);
            var handlers = GraphClientFactory.CreateDefaultHandlers();
            handlers.Add(chaosHandler);
            var httpClient = GraphClientFactory.Create(handlers);
            httpClient.Timeout = TimeSpan.FromSeconds(300);
            return new GraphServiceClient(httpClient);
        }

        private string GetGroupRawResponse(string groupId)
        {
            return $"{{\"@odata.context\":\"https://graph.microsoft.com/v1.0/$metadata#groups/$entity\"," +
                                      $"\"id\":\"{groupId}\",\"deletedDateTime\":null,\"classification\":null,\"createdDateTime\":\"2023-01-01T00:00:00Z\"," +
                                      $"\"creationOptions\":[],\"description\":\"test group\",\"displayName\":\"test group\",\"expirationDateTime\":null," +
                                      $"\"groupTypes\":[\"Unified\"],\"isAssignableToRole\":null,\"mail\":\"test@test.com\",\"mailEnabled\":true," +
                                      $"\"mailNickname\":\"test\",\"membershipRule\":null,\"membershipRuleProcessingState\":null,\"onPremisesDomainName\":null," +
                                      $"\"onPremisesLastSyncDateTime\":null,\"onPremisesNetBiosName\":null,\"onPremisesSamAccountName\":null," +
                                      $"\"onPremisesSecurityIdentifier\":null,\"onPremisesSyncEnabled\":null,\"preferredDataLocation\":null," +
                                      $"\"preferredLanguage\":null,\"proxyAddresses\":[],\"renewedDateTime\":\"2023-01-01T00:00:00Z\"," +
                                      $"\"resourceBehaviorOptions\":[],\"resourceProvisioningOptions\":[],\"securityEnabled\":true," +
                                      $"\"securityIdentifier\":\"\",\"theme\":null,\"visibility\":\"Private\",\"onPremisesProvisioningErrors\":[]," +
                                      $"\"serviceProvisioningErrors\":[]}}";
        }

        private HttpResponseMessage GetSampleResponse(bool isMailEnabled,
                                                      bool isSecurityEnabled,
                                                      string[] groupTypes,
                                                      Dictionary<string, string> requestIds)
        {

            groupTypes = groupTypes.Select(x => $"\"{x}\"").ToArray();

            var jsonResponses = new List<string>();
            var outlookResponse = $"{{ \"id\": \"{requestIds["outlook"]}\", " +
                                        $"\"status\": 200,  " +
                                        $"\"body\": " +
                                        $"{{ " +
                                            $"\"mailEnabled\": {isMailEnabled.ToString().ToLower()}," +
                                            $"\"securityEnabled\": {isSecurityEnabled.ToString().ToLower()}," +
                                            $"\"groupTypes\": [{string.Join(",", groupTypes)}]" +
                                        $"}}  " +
                                    $"}}";

            jsonResponses.Add(outlookResponse);

            var sharepointResponse = $"{{ \"id\": \"{requestIds["sharepoint"]}\", " +
                                    $"\"status\": 200,  " +
                                    $"\"body\": " +
                                    $"{{ " +
                                        $"\"webUrl\": \"someUrl\"" +
                                    $"}}  " +
                                $"}}";

            jsonResponses.Add(sharepointResponse);
            var jsonResponse = $"{{\"responses\": [{string.Join(",", jsonResponses)}]}}";
            return GenerateHttpResponseMessage(jsonResponse);
        }

        private HttpResponseMessage GenerateHttpResponseMessage(string httpContent)
        {
            var httpResponse = new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(httpContent)
            };

            return httpResponse;
        }

        private static IEnumerable<object> GetEndPointsData()
        {
            var responses = new List<object[]>();

            // M365 Group with providers
            responses.Add(new object[]
            {
                Guid.Parse("00000000-0000-0000-0000-000000000001"), // group id
                true,                                               // mail enabled
                false,                                              // security enabled
                new string[] { "Unified" },                         // group types
                new string[] { "Yammer", "Teams" }                  // providers
            });

            //M365 Group with no providers
            responses.Add(new object[]
            {
                Guid.Parse("00000000-0000-0000-0000-000000000002"), // group id
                true,                                               // mail enabled
                false,                                              // security enabled
                new string[] { "Unified" },                         // group types
                new string[] { }                                    // providers
            });

            // SecurityGroup
            responses.Add(new object[]
            {
                Guid.Parse("00000000-0000-0000-0000-000000000003"), // group id
                false,                                              // mail enabled
                true,                                               // security enabled
                new string[] { },                                   // group types
                new string[] { }                                    // providers
            });

            return responses;
        }
    }
}
