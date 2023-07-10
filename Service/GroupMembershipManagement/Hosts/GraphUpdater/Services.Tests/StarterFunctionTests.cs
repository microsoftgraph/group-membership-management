// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Hosts.GraphUpdater;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;
using Newtonsoft.Json;
using Repositories.Mocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class StarterFunctionTests
    {
        private string _instanceId;
        private MockLoggingRepository _loggerMock;
        private Mock<IDurableOrchestrationClient> _durableClientMock;
        private SyncJob _syncJob;

        [TestInitialize]
        public void SetupTest()
        {
            _instanceId = "1234567890";
            _durableClientMock = new Mock<IDurableOrchestrationClient>();
            _loggerMock = new MockLoggingRepository();
            _syncJob = new SyncJob
            {                
                Id = Guid.NewGuid(),
                TargetOfficeGroupId = Guid.NewGuid(),
                ThresholdPercentageForAdditions = 80,
                ThresholdPercentageForRemovals = 20,
                LastRunTime = DateTime.UtcNow.AddDays(-1),
                Requestor = "user@domail.com",
                RunId = Guid.NewGuid(),
                ThresholdViolations = 0
            };
        }

        [TestMethod]
        public async Task ProcessValidRequestTest()
        {
            _durableClientMock
                .Setup(x => x.StartNewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MembershipHttpRequest>()))
                .ReturnsAsync(_instanceId);

            var starterFunction = new StarterFunction(_loggerMock);
            var groupMembership = GetGroupMembership();
            var content = new MembershipHttpRequest
            {
                FilePath = "file/path/name.json",
                SyncJob = _syncJob
            };

            var contentBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(content));
            var properties = new Dictionary<string, object>
            {
                { "Type", "SecurityGroup"}
            };

            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(contentBytes), properties: properties);

            await starterFunction.RunAsync(message, _durableClientMock.Object);

            Assert.IsNotNull(_loggerMock.MessagesLogged.Single(x => x.Message.Contains("function started")));
            Assert.IsNotNull(_loggerMock.MessagesLogged.Single(x => x.Message.Contains("InstanceId:")));
            Assert.IsNotNull(_loggerMock.MessagesLogged.Single(x => x.Message.Contains("function complete")));
        }

        private string GetMembershipBody(int totalMessageCount = 1)
        {
            var json =
            "{" +
            "  'Sources': [" +
            "    {" +
            "      'ObjectId': '8032abf6-b4b1-45b1-8e7e-40b0bd16d6eb'" +
            "    }" +
            "  ]," +
            "  'Destination': {" +
            "    'ObjectId': 'dc04c21f-091a-44a9-a661-9211dd9ccf35'" +
            "  }," +
            "  'SourceMembers': []," +
            "  'RunId': '501f6c70-8fe1-496f-8446-befb15b5249a'," +
            "  'SyncJobId': '0a4cc250-69a0-4019-8298-96bf492aca01'," +
            "  'Errored': false," +
            "  'TotalMessageCount': " + totalMessageCount.ToString() +
            "}";

            return json;
        }

        private GroupMembership GetGroupMembership(int totalMessageCount = 1)
        {
            var json = GetMembershipBody(totalMessageCount);
            var groupMembership = JsonConvert.DeserializeObject<GroupMembership>(json);

            return groupMembership;
        }
    }
}
