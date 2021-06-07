// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using AzureUserReader.Requests;
using AzureUserReader.Starter;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Repositories.Contracts;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class StarterFunctionTests
    {
        private string _instanceId;
        private Mock<ILoggingRepository> _loggerMock;
        private Mock<IDurableOrchestrationClient> _durableClientMock;

        [TestInitialize]
        public void SetupTest()
        {
            _instanceId = "1234567890";
            _durableClientMock = new Mock<IDurableOrchestrationClient>();
            _loggerMock = new Mock<ILoggingRepository>();
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("abc")]
        [DataRow("[{ 'a': 1 }]")]
        [DataRow("{ 'BlobPath':'folder1/folder2/myfile.csv' }")]
        public async Task PostInvalidRequest(string content)
        {
            var durableClientMock = new Mock<IDurableOrchestrationClient>();
            var loggerMock = new Mock<ILoggingRepository>();

            durableClientMock
                    .Setup(x => x.StartNewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AzureUserReaderRequest>()))
                    .ReturnsAsync(_instanceId);

            var starterFunction = new StarterFunction(loggerMock.Object);
            var result = await starterFunction.HttpStart(
                new HttpRequestMessage()
                {
                    Content = new StringContent(content, Encoding.UTF8, "application/json")
                },
                durableClientMock.Object
               );

            Assert.AreEqual(HttpStatusCode.BadRequest, result.StatusCode);
        }

        [TestMethod]
        public async Task PostValidRequest()
        {
            _durableClientMock
                    .Setup(x => x.StartNewAsync(It.IsAny<string>(), It.IsAny<AzureUserReaderRequest>()))
                    .ReturnsAsync(_instanceId);

            _durableClientMock
                .Setup(x => x.CreateCheckStatusResponse(It.IsAny<HttpRequestMessage>(), _instanceId, It.IsAny<bool>()))
                .Returns(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(string.Empty)
                });

            var starterFunction = new StarterFunction(_loggerMock.Object);
            var result = await starterFunction.HttpStart(
                new HttpRequestMessage()
                {
                    Content = new StringContent(
                        "{ 'ContainerName':'myContainer','BlobPath':'folder1/folder2/myfile.csv'}",
                                Encoding.UTF8, "application/json")
                },
                _durableClientMock.Object
               );

            Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        }
    }
}