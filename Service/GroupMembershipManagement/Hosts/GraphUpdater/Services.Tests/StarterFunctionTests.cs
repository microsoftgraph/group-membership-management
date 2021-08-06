// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Hosts.GraphUpdater;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json.Linq;
using Repositories.Mocks;
using Services.Contracts;
using Services.Entities;
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
        private Mock<IMessageSession> _messageSessionMock;
        private Mock<IServiceBusMessageService> _messageService;

        [TestInitialize]
        public void SetupTest()
        {
            _instanceId = "1234567890";
            _durableClientMock = new Mock<IDurableOrchestrationClient>();
            _loggerMock = new MockLoggingRepository();
            _messageSessionMock = new Mock<IMessageSession>();
            _messageService = new Mock<IServiceBusMessageService>();
        }

        [TestMethod]
        public async Task ProcessSingleMessageSuccessTest()
        {
            var messageDetails = new MessageInformation
            {
                Body = Encoding.UTF8.GetBytes(GetMessageBody()),
                LockToken = Guid.NewGuid().ToString(),
                SessionId = "dc04c21f-091a-44a9-a661-9211dd9ccf35"
            };

            var output = new GroupMembershipMessageResponse
            {
                CompletedGroupMembershipMessages = new List<GroupMembershipMessage>
                {
                    new GroupMembershipMessage { LockToken = messageDetails.LockToken }
                },
                ShouldCompleteMessage = true
            };

            var status = new DurableOrchestrationStatus
            {
                RuntimeStatus = OrchestrationRuntimeStatus.Completed,
                Output = JToken.FromObject(output)
            };

            _durableClientMock
                  .Setup(x => x.StartNewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GraphUpdaterFunctionRequest>()))
                  .ReturnsAsync(_instanceId);

            _durableClientMock
                .Setup(x => x.GetStatusAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync(status);

            _messageService.Setup(x => x.GetMessageProperties(It.IsAny<Message>())).Returns(messageDetails);

            var starterFunction = new StarterFunction(_loggerMock, _messageService.Object);
            await starterFunction.RunAsync(new Message(), _durableClientMock.Object, _messageSessionMock.Object);

            _messageSessionMock.Verify(mock => mock.CompleteAsync(It.IsAny<IEnumerable<string>>()), Times.Once());
            _messageSessionMock.Verify(mock => mock.CloseAsync(), Times.Once());

            Assert.IsNotNull(_loggerMock.MessagesLogged.Single(x => x.Message.Contains("Instance processing completed")));
            Assert.IsNotNull(_loggerMock.MessagesLogged.Single(x => x.Message.Contains("function complete")));
        }

        [TestMethod]
        public async Task ProcessSingleMessageFailTest()
        {
            var messageDetails = new MessageInformation
            {
                Body = Encoding.UTF8.GetBytes(GetMessageBody()),
                LockToken = Guid.NewGuid().ToString(),
                SessionId = "dc04c21f-091a-44a9-a661-9211dd9ccf35"
            };

            var output = new GroupMembershipMessageResponse
            {
                CompletedGroupMembershipMessages = new List<GroupMembershipMessage>
                {
                    new GroupMembershipMessage { LockToken = messageDetails.LockToken }
                },
                ShouldCompleteMessage = true
            };

            var status = new DurableOrchestrationStatus
            {
                RuntimeStatus = OrchestrationRuntimeStatus.Running,
                Output = JToken.FromObject(output)
            };

            _durableClientMock
                  .Setup(x => x.StartNewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GraphUpdaterFunctionRequest>()))
                  .ReturnsAsync(_instanceId);

            var attempt = 1;
            _durableClientMock
                .Setup(x => x.GetStatusAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Callback(() =>
                    {
                        if (attempt > 1)
                            status.RuntimeStatus = OrchestrationRuntimeStatus.Terminated;

                        attempt++;
                    })
                .ReturnsAsync(status);

            _messageService.Setup(x => x.GetMessageProperties(It.IsAny<Message>())).Returns(messageDetails);

            var starterFunction = new StarterFunction(_loggerMock, _messageService.Object);
            await starterFunction.RunAsync(new Message(), _durableClientMock.Object, _messageSessionMock.Object);

            _messageSessionMock.Verify(mock => mock.CompleteAsync(It.IsAny<IEnumerable<string>>()), Times.Never());
            _messageSessionMock.Verify(mock => mock.CloseAsync(), Times.Never());

            Assert.IsNotNull(_loggerMock.MessagesLogged.Single(x => x.Message.Contains("Error: Status of instance")));
            Assert.IsNotNull(_loggerMock.MessagesLogged.Single(x => x.Message.Contains("function complete")));
        }

        private string GetMessageBody()
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
            "  'SyncJobRowKey': '0a4cc250-69a0-4019-8298-96bf492aca01'," +
            "  'SyncJobPartitionKey': '2021-01-01'," +
            "  'Errored': false," +
            "  'IsLastMessage': true" +
            "}";

            return json;
        }
    }
}
