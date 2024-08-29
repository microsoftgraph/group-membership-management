// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.AzureUserReader;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Repositories.Contracts;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Repositories.Mocks;

namespace Services.Tests
{
    [TestClass]
    public class StarterFunctionTests
    {
        private string _instanceId;
        private Mock<ILoggingRepository> _loggerMock;
        private Mock<DurableTaskClient> _durableClientMock;

        [TestInitialize]
        public void SetupTest()
        {
            _instanceId = "1234567890";
            _durableClientMock = new Mock<DurableTaskClient>();
            _loggerMock = new Mock<ILoggingRepository>();
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("abc")]
        [DataRow("[{ 'a': 1 }]")]
        [DataRow("{ 'BlobPath':'folder1/folder2/myfile.csv' }")]
        public async Task PostInvalidRequest(string content)
        {
            var durableClientMock = new Mock<MockDurableTaskClient>();
            var loggerMock = new Mock<ILoggingRepository>();

            var mockContext = new Mock<FunctionContext>();

            var mockRequest = new MockHttpRequestData(mockContext.Object,
                            content);
            var starterFunction = new StarterFunction(loggerMock.Object);

            var result = await starterFunction.HttpStart(mockRequest, durableClientMock.Object);
            Assert.AreEqual(HttpStatusCode.BadRequest, result.StatusCode);
        }

        public async Task PostValidRequest()
        {
            var instanceId = "test-instance-id";
            var durableClientMock = new Mock<MockDurableTaskClient>();
            var loggerMock = new Mock<ILoggingRepository>();

            durableClientMock
                .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), It.IsAny<AzureUserReaderRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(instanceId);

            var mockContext = new Mock<FunctionContext>();

            var mockRequest = new MockHttpRequestData(mockContext.Object, "{ 'ContainerName':'myContainer','BlobPath':'folder1/folder2/myfile.csv'}");

            var starterFunction = new StarterFunction(loggerMock.Object);

            var result = await starterFunction.HttpStart(mockRequest, durableClientMock.Object);

            Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        }
    }
}