// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Hosts.MembershipAggregator;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Newtonsoft.Json;
using Repositories.Contracts;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class StarterFunctionTests
    {
        private SyncJob _syncJob;
        private string _instanceId;
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<IDurableOrchestrationClient> _durableClient;

        [TestInitialize]
        public void SetupTest()
        {
            _instanceId = "1234567890";
            _loggingRepository = new Mock<ILoggingRepository>();
            _durableClient = new Mock<IDurableOrchestrationClient>();
            _syncJob = new SyncJob
            {
                PartitionKey = "00-00-0000",
                RowKey = Guid.NewGuid().ToString(),
                TargetOfficeGroupId = Guid.NewGuid(),
                ThresholdPercentageForAdditions = 80,
                ThresholdPercentageForRemovals = 20,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid(),
                ThresholdViolations = 0
            };

            _durableClient
                  .Setup(x => x.StartNewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MembershipAggregatorHttpRequest>()))
                  .ReturnsAsync(_instanceId);
        }

        [TestMethod]
        public async Task ProcessEmptyRequestTestAsync()
        {
            var starterFunction = new StarterFunction(_loggingRepository.Object);
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post
            };

            var response = await starterFunction.RunAsync(request, _durableClient.Object);
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("Request content is empty.", responseContent);
            _loggingRepository.Verify(x => x.LogMessageAsync(
                                                It.Is<LogMessage>(m => m.Message.StartsWith("MembershipAggregator instance id")),
                                                VerbosityLevel.INFO,
                                                It.IsAny<string>(),
                                                It.IsAny<string>()), Times.Never());
        }

        [TestMethod]
        public async Task ProcessInvalidRequestTestAsync()
        {
            var starterFunction = new StarterFunction(_loggingRepository.Object);
            var content = new MembershipHttpRequest
            {
                FilePath = null,
                SyncJob = _syncJob
            };

            var request = new HttpRequestMessage
            {
                Content = new StringContent(JsonConvert.SerializeObject(content)),
            };

            var response = await starterFunction.RunAsync(request, _durableClient.Object);
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("Request is not valid.", responseContent);
            _loggingRepository.Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(m => m.Message.StartsWith("MembershipAggregator instance id")),
                                    VerbosityLevel.INFO,
                                    It.IsAny<string>(),
                                    It.IsAny<string>()), Times.Never());
        }

        [TestMethod]
        public async Task ProcessValidRequestTestAsync()
        {
            var starterFunction = new StarterFunction(_loggingRepository.Object);
            var content = new MembershipAggregatorHttpRequest
            {
                FilePath = "file/path/name.json",
                SyncJob = _syncJob,
                PartNumber = 1,
                PartsCount = 1
            };

            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                Content = new StringContent(JsonConvert.SerializeObject(content)),
            };

            var response = await starterFunction.RunAsync(request, _durableClient.Object);

            Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
            var contentString = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(0, contentString.Length);
            _loggingRepository.Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(m => m.Message.StartsWith("MembershipAggregator instance id")),
                                    VerbosityLevel.INFO,
                                    It.IsAny<string>(),
                                    It.IsAny<string>()), Times.Once());
        }

        [TestMethod]
        public async Task ProcessServiceBusMessageAsync()
        {
            var starterFunction = new StarterFunction(_loggingRepository.Object);
            var content = new MembershipAggregatorHttpRequest
            {
                FilePath = "file/path/name.json",
                SyncJob = _syncJob,
                PartNumber = 1,
                PartsCount = 1
            };

            var contentBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(content));
            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(contentBytes));

            await starterFunction.ProcessServiceBusMessageAsync(message, _durableClient.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message.StartsWith("Processing message")),
                        VerbosityLevel.INFO,
                        It.IsAny<string>(),
                        It.IsAny<string>()), Times.Once());
        }
    }
}
