// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Graph;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphAzureADUsers;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Xunit;

namespace Graph.Tests
{
    [ExcludeFromCodeCoverage]
    public class GraphUserRepositoryTests
    {
        private const string HTTP_CONTENT_PATH = "./SampleData/graph-response.json";
        [Fact(DisplayName = "Constructor tests")]
        [Trait("Category", "Unit")]
        public void ConstructorTests()
        {
            var mockGraphClient = new Mock<IGraphServiceClient>().Object;
            var mockLoggingRepository = new Mock<ILoggingRepository>().Object;
            var mockServiceAttemptValue = new Mock<IGraphServiceAttemptsValue>().Object;

            Assert.Throws<ArgumentNullException>(() => new GraphUserRepository(null, mockGraphClient, mockServiceAttemptValue));
            Assert.Throws<ArgumentNullException>(() => new GraphUserRepository(mockLoggingRepository, null, mockServiceAttemptValue));
            var repo = new GraphUserRepository(mockLoggingRepository, mockGraphClient, mockServiceAttemptValue);
            Assert.NotNull(repo);
        }

        [Fact(DisplayName = "Get profiles test")]
        [Trait("Category", "Unit")]
        public async Task GetProfilesTest()
        {
            var mockBaseClient = new Mock<IBaseClient>();
            mockBaseClient.Setup(c => c.AuthenticationProvider).Returns(new Mock<IAuthenticationProvider>().Object);

            var requestMessage = new HttpRequestMessage
            {
                RequestUri = new Uri("https://graph.contoso.com/v1/fakerequest")
            };

            var mockRequest = new Mock<IGraphServiceUsersCollectionRequest>();
            mockRequest.Setup(r => r.Filter(It.IsAny<string>())).Returns(mockRequest.Object);
            mockRequest.Setup(r => r.Select(It.IsAny<string>())).Returns(mockRequest.Object);
            mockRequest.Setup(r => r.MiddlewareOptions).Returns(new Dictionary<string, IMiddlewareOption>());
            mockRequest.Setup(r => r.Client).Returns(mockBaseClient.Object);
            mockRequest.Setup(r => r.GetHttpRequestMessage()).Returns(requestMessage);

            var mockUsers = new Mock<IGraphServiceUsersCollectionRequestBuilder>();
            mockUsers.Setup(o => o.Request()).Returns(mockRequest.Object);

            var mockAuthProvider = new Mock<IAuthenticationProvider>();
            mockAuthProvider.Setup(p => p.AuthenticateRequestAsync(It.IsAny<HttpRequestMessage>())).Returns(Task.CompletedTask);

            StringContent content;
            using (var reader = System.IO.File.OpenText(HTTP_CONTENT_PATH))
            {
                content = new StringContent(reader.ReadToEnd());
            }

            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = content };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(10));

            var mockHttpProvider = new Mock<IHttpProvider>();
            int retryCount = 0;
            mockHttpProvider.Setup(p => p.SendAsync(It.IsAny<HttpRequestMessage>())).Callback<HttpRequestMessage>(request =>
            {
                if (retryCount++ == 3)
                {
                    response.StatusCode = HttpStatusCode.OK;
                }

            }).Returns(Task.FromResult(response));

            var httpProvider = mockHttpProvider.Object;

            var mockGraphClient = new Mock<IGraphServiceClient>();
            mockGraphClient.Setup(c => c.Users).Returns(mockUsers.Object);
            mockGraphClient.Setup(c => c.AuthenticationProvider).Returns(mockAuthProvider.Object);
            mockGraphClient.Setup(c => c.HttpProvider).Returns(httpProvider);

            var mockLoggingRepository = new Mock<ILoggingRepository>();
            var mockServiceAttemptValue = new Mock<IGraphServiceAttemptsValue>();
            mockServiceAttemptValue.SetupGet(x => x.MaxRetryAfterAttempts).Returns(5);

            var repo = new GraphUserRepository(mockLoggingRepository.Object, mockGraphClient.Object, mockServiceAttemptValue.Object);
            var personnelNumbers = new List<string>
            {
                "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13"
            };

            await repo.GetAzureADObjectIdsAsync(personnelNumbers, Guid.NewGuid());
            mockHttpProvider.Verify(x => x.SendAsync(It.IsAny<HttpRequestMessage>()), Times.Exactly(4)); // 1st time actual call + 3 retries
        }
    }
}