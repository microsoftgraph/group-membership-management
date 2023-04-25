// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Repositories.Contracts;
using Repositories.GraphGroups;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class GraphGroupRepositoryTests
    {
        private TelemetryClient _telemetryClient = new TelemetryClient(new TelemetryConfiguration("instrumentationkey"));
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<GraphServiceClient> _graphServiceClient;
        private GraphGroupRepository _graphGroupRepository;
        private Mock<IRequestAdapter> _requestAdapter;
        private Mock<IResponseHandler> _responseHandler;
        private HttpResponseMessage _response;

        [TestInitialize]
        public void InitializeTest()
        {
            _loggingRepository = new Mock<ILoggingRepository>();

            _requestAdapter = new Mock<IRequestAdapter>();
            _responseHandler = new Mock<IResponseHandler>();

            _graphServiceClient = new Mock<GraphServiceClient>(_requestAdapter.Object, "https://graph.microsoft.com");
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
                        var content = GetSampleResponse(isMailEnabled, isSecurityEnabled, groupTypes);
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


            var endPoints = await _graphGroupRepository.GetGroupEndpointsAsync(groupId);

            Assert.IsTrue(endPoints.Contains("Outlook") || endPoints.Contains("SecurityGroup"));

            if (providers.Length > 0)
            {
                Assert.IsTrue(providers.All(x => endPoints.Contains(x)));
            }
        }

        private HttpResponseMessage GetSampleResponse(bool isMailEnabled,
                                                      bool isSecurityEnabled,
                                                      string[] groupTypes)
        {

            groupTypes = groupTypes.Select(x => $"\"{x}\"").ToArray();

            var jsonResponses = new List<string>();
            var outlookResponse = $"{{ \"id\": \"outlook\", " +
                                        $"\"status\": 200,  " +
                                        $"\"body\": " +
                                        $"{{ " +
                                            $"\"mailEnabled\": {isMailEnabled.ToString().ToLower()}," +
                                            $"\"securityEnabled\": {isSecurityEnabled.ToString().ToLower()}," +
                                            $"\"groupTypes\": [{string.Join(",", groupTypes)}]" +
                                        $"}}  " +
                                    $"}}";

            jsonResponses.Add(outlookResponse);

            var sharepointResponse = $"{{ \"id\": \"sharepoint\", " +
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

            // M365 Group with no providers
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
