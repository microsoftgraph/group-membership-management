// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Graph;
using Microsoft.Graph.Core.Requests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using Repositories.GraphGroups;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class GraphGroupRepositoryTests
    {
        private TelemetryClient _telemetryClient = new TelemetryClient(new TelemetryConfiguration("instrumentationkey"));
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<IGraphServiceClient> _graphServiceClient;
        private GraphGroupRepository _graphGroupRepository;

        [TestInitialize]
        public void InitializeTest()
        {
            _loggingRepository = new Mock<ILoggingRepository>();
            _graphServiceClient = new Mock<IGraphServiceClient>();
            _graphGroupRepository = new GraphGroupRepository(_graphServiceClient.Object, _telemetryClient, _loggingRepository.Object);

            var authenticationProvider = new Mock<IAuthenticationProvider>();
            authenticationProvider.Setup(x => x.AuthenticateRequestAsync(It.IsAny<HttpRequestMessage>()));
            _graphServiceClient.Setup(x => x.AuthenticationProvider).Returns(authenticationProvider.Object);
        }

        [TestMethod]
        [DynamicData(nameof(GetEndPointsData), DynamicDataSourceType.Method)]
        public async Task TestGetGroupEndpointsAsync(Guid groupId, bool isMailEnabled, bool isSecurityEnabled, string[] groupTypes, string[] providers)
        {
            var batchRequest = new Mock<IBatchRequest>();
            var batch = new Mock<IBatchRequestBuilder>();
            BatchResponseContent batchResponseContent = null;

            batch.Setup(x => x.Request()).Returns(batchRequest.Object);
            _graphServiceClient.Setup(x => x.Batch).Returns(batch.Object);

            batchRequest.Setup(x => x.PostAsync(It.IsAny<BatchRequestContent>()))
                        .Callback<BatchRequestContent>((request) =>
                        {
                            var jsonResponses = new List<string>();
                            foreach (var step in request.BatchRequestSteps)
                            {
                                if (step.Key.Equals("outlook"))
                                {
                                    var query = step.Value.Request.RequestUri.Query.Replace("?$select=", string.Empty);
                                    var properties = query.Split(",", StringSplitOptions.RemoveEmptyEntries);

                                    if (!properties.Contains("mailEnabled"))
                                        isMailEnabled = false;

                                    if (!properties.Contains("securityEnabled"))
                                        isSecurityEnabled = false;

                                    if (!properties.Contains("groupTypes"))
                                        groupTypes = new string[0];
                                    else
                                        groupTypes = groupTypes.Select(x => $"\"{x}\"").ToArray();

                                    var outlookResponse = $"{{ \"id\": \"{step.Key}\", " +
                                                                $"\"status\": 200,  " +
                                                                $"\"body\": " +
                                                                $"{{ " +
                                                                    $"\"mailEnabled\": {isMailEnabled.ToString().ToLower()}," +
                                                                    $"\"securityEnabled\": {isSecurityEnabled.ToString().ToLower()}," +
                                                                    $"\"groupTypes\": [{string.Join(",", groupTypes)}]" +
                                                                $"}}  " +
                                                            $"}}";

                                    jsonResponses.Add(outlookResponse);
                                }
                                else if (step.Key.Equals("sharepoint"))
                                {
                                    var sharepointResponse = $"{{ \"id\": \"{step.Key}\", " +
                                                            $"\"status\": 200,  " +
                                                            $"\"body\": " +
                                                            $"{{ " +
                                                                $"\"webUrl\": \"someUrl\"" +
                                                            $"}}  " +
                                                        $"}}";

                                    jsonResponses.Add(sharepointResponse);
                                }
                            }

                            var jsonResponse = $"{{\"responses\": [{string.Join(",", jsonResponses)}]}}";
                            batchResponseContent = GenerateBatchResponseContent(jsonResponse);
                        })
                        .ReturnsAsync(() => batchResponseContent);

            var httpProvider = new Mock<IHttpProvider>();
            httpProvider.Setup(x => x.SendAsync(It.IsAny<HttpRequestMessage>())).ReturnsAsync((Func<HttpResponseMessage>)(() =>
            {
                var providersJSON = string.Join(",", Enumerable.Select<string, string>(providers, (Func<string, string>)(x => $"{{\"providerName\":\"{x}\"}}")));
                var content = $"{{ \"value\":[ {providersJSON} ] }}";
                return new HttpResponseMessage
                {
                    Content = new StringContent(content),
                    StatusCode = System.Net.HttpStatusCode.OK
                };
            }));

            _graphServiceClient.Setup(x => x.HttpProvider).Returns(httpProvider.Object);

            var endPoints = await _graphGroupRepository.GetGroupEndpointsAsync(groupId);

            Assert.IsTrue(endPoints.Contains("Outlook") || endPoints.Contains("SecurityGroup"));

            if (providers.Length > 0)
            {
                Assert.IsTrue(providers.All(x => endPoints.Contains(x)));
            }
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
