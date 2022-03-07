// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Hosts.GraphUpdater;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json;
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
        private Mock<IConfiguration> _configuration;

        [TestInitialize]
        public void SetupTest()
        {
            _instanceId = "1234567890";
            _durableClientMock = new Mock<IDurableOrchestrationClient>();
            _loggerMock = new MockLoggingRepository();
            _messageSessionMock = new Mock<IMessageSession>();
            _messageService = new Mock<IServiceBusMessageService>();
            _configuration = new Mock<IConfiguration>();
        }

        [TestMethod]
        public async Task ProcessSingleMessageSuccessTest()
        {
            var status = new DurableOrchestrationStatus
            {
                RuntimeStatus = OrchestrationRuntimeStatus.Completed,
                Output = ""
            };

            _durableClientMock
                .Setup(x => x.StartNewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GraphUpdaterFunctionRequest>()))
                .ReturnsAsync(_instanceId);

            _durableClientMock
                .Setup(x => x.GetStatusAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync(status);

            _messageSessionMock
                .Setup(x => x.SessionId)
                .Returns(GetGroupMembership().RunId.ToString());

            _messageService
                .Setup(x => x.GetMessageProperties(It.IsAny<Message>()))
                .Returns(new MessageInformation { Body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(GetGroupMembership())) });

            var starterFunction = new StarterFunction(_loggerMock, _messageService.Object, _configuration.Object);

            await starterFunction.RunAsync(new Message[] { new Message {
                    Body = Encoding.UTF8.GetBytes(GetMembershipBody()),
                    SessionId = GetGroupMembership().RunId.ToString(),
                    ContentType = "application/json",
                    Label = ""
                } },
                _durableClientMock.Object,
                _messageSessionMock.Object);

            _messageSessionMock.Verify(mock => mock.CompleteAsync(It.IsAny<IEnumerable<string>>()), Times.Once());
            _messageSessionMock.Verify(mock => mock.CloseAsync(), Times.Once());

            Assert.IsNotNull(_loggerMock.MessagesLogged.Single(x => x.Message.Contains("Instance processing completed")));
            Assert.IsNotNull(_loggerMock.MessagesLogged.Single(x => x.Message.Contains("function complete")));
        }

        [TestMethod]
        public async Task ProcessSingleMessageFailTest()
        {
            var status = new DurableOrchestrationStatus
            {
                RuntimeStatus = OrchestrationRuntimeStatus.Failed,
                Output = ""
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
            
            _messageSessionMock
                .Setup(x => x.SessionId)
                .Returns(GetGroupMembership().RunId.ToString());

            _messageService
                .Setup(x => x.GetMessageProperties(It.IsAny<Message>()))
                .Returns(new MessageInformation { Body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(GetGroupMembership())) });

            var starterFunction = new StarterFunction(_loggerMock, _messageService.Object, _configuration.Object);

            await starterFunction.RunAsync(new Message[] { new Message {
                    Body = Encoding.UTF8.GetBytes(GetMembershipBody()),
                    SessionId = GetGroupMembership().RunId.ToString(),
                    ContentType = "application/json",
                    Label = ""
                } },
                _durableClientMock.Object,
                _messageSessionMock.Object);

            _messageSessionMock.Verify(mock => mock.CompleteAsync(It.IsAny<IEnumerable<string>>()), Times.Once());
            _messageSessionMock.Verify(mock => mock.CloseAsync(), Times.Once());

            Assert.IsNotNull(_loggerMock.MessagesLogged.Single(x => x.Message.Contains("Error: Status of instance")));
            Assert.IsNotNull(_loggerMock.MessagesLogged.Single(x => x.Message.Contains("function complete")));
        }

        [TestMethod]
        [Timeout(180000)]
        public async Task ProcessSessionWithNoLastMessageTest()
        {
            var status = new DurableOrchestrationStatus
            {
                RuntimeStatus = OrchestrationRuntimeStatus.Completed,
                Output = ""
            };

            _messageSessionMock
                .Setup(x => x.SessionId)
                .Returns(GetGroupMembership().RunId.ToString());

            _messageSessionMock
                .Setup(x => x.ReceiveAsync(1000))
                .ReturnsAsync(new Message[] { new Message {
                        Body = Encoding.UTF8.GetBytes(GetMembershipBody(1000)),
                        SessionId = GetGroupMembership(1000).RunId.ToString(),
                        ContentType = "application/json",
                        Label = ""
                    } }
                );

            _messageService
                .Setup(x => x.GetMessageProperties(It.IsAny<Message>()))
                .Returns(new MessageInformation { Body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(GetGroupMembership(1000))) });

            _configuration.SetupGet(x => x["GraphUpdater:LastMessageWaitTimeout"]).Returns("1");

            var cancelationRequestCount = 0;

            _durableClientMock
                 .Setup(x => x.StartNewAsync(It.IsAny<string>(), It.IsAny<GraphUpdaterFunctionRequest>()))
                 .Callback<string, object>((name, request) =>
                 {
                     var graphUpdaterRequest = request as GraphUpdaterFunctionRequest;
                     if (graphUpdaterRequest != null && graphUpdaterRequest.IsCancelationRequest)
                         cancelationRequestCount++;
                 })
                 .ReturnsAsync(_instanceId);

            _durableClientMock
                .Setup(x => x.GetStatusAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync(status);

            var starterFunction = new StarterFunction(_loggerMock, _messageService.Object, _configuration.Object);

            await starterFunction.RunAsync(new Message[] { new Message {
                    Body = Encoding.UTF8.GetBytes(GetMembershipBody(1000)),
                    SessionId = GetGroupMembership(1000).RunId.ToString(),
                    ContentType = "application/json",
                    Label = ""
                } },
                _durableClientMock.Object,
                _messageSessionMock.Object);

            _messageSessionMock.Verify(mock => mock.CompleteAsync(It.IsAny<IEnumerable<string>>()), Times.Once());
            _messageSessionMock.Verify(mock => mock.CloseAsync(), Times.Once());

            _durableClientMock.Verify(x => x.StartNewAsync(It.IsAny<string>(), It.IsAny<GraphUpdaterFunctionRequest>()), Times.Exactly(1));
            Assert.AreEqual(1, cancelationRequestCount);
        }

        [TestMethod]
        [Timeout(180000)]
        public async Task ProcessSessionWithLastMessageTest()
        {
            var totalMessages = 10;
            var messagesReceivedSoFar = 1;

            var status = new DurableOrchestrationStatus
            {
                RuntimeStatus = OrchestrationRuntimeStatus.Completed,
                Output = ""
            };

            _durableClientMock
                .Setup(x => x.StartNewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GraphUpdaterFunctionRequest>()))
                .ReturnsAsync(_instanceId);

            _durableClientMock
                .Setup(x => x.GetStatusAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync(status);

            _messageSessionMock
                .Setup(x => x.SessionId)
                .Returns(GetGroupMembership().RunId.ToString());

            _messageSessionMock
                .Setup(x => x.ReceiveAsync(It.IsAny<int>()))
                .ReturnsAsync(() => {
                    messagesReceivedSoFar++;

                    return new Message[] { new Message {
                            Body = Encoding.UTF8.GetBytes(GetMembershipBody(totalMessages)),
                            SessionId = GetGroupMembership(totalMessages).RunId.ToString(),
                            ContentType = "application/json",
                            Label = ""
                        }
                    };
                    }
                );

            _messageService
                .Setup(x => x.GetMessageProperties(It.IsAny<Message>()))
                .Returns(() => {
                    return new MessageInformation
                    {
                        Body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(GetGroupMembership(totalMessages)))
                    };
                });

            var starterFunction = new StarterFunction(_loggerMock, _messageService.Object, _configuration.Object);

            await starterFunction.RunAsync(new Message[] { new Message {
                    Body = Encoding.UTF8.GetBytes(GetMembershipBody(totalMessages)),
                    SessionId = GetGroupMembership(totalMessages).RunId.ToString(),
                    ContentType = "application/json",
                    Label = ""
                } },
                _durableClientMock.Object,
                _messageSessionMock.Object);

            _messageSessionMock.Verify(mock => mock.ReceiveAsync(1000), Times.Exactly(9));
            _messageSessionMock.Verify(mock => mock.CompleteAsync(It.IsAny<IEnumerable<string>>()), Times.Once());
            _messageSessionMock.Verify(mock => mock.CloseAsync(), Times.Once());
            _durableClientMock.Verify(x => x.StartNewAsync(It.IsAny<string>(), It.IsAny<GraphUpdaterFunctionRequest>()), Times.Once());
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
            "  'SyncJobRowKey': '0a4cc250-69a0-4019-8298-96bf492aca01'," +
            "  'SyncJobPartitionKey': '2021-01-01'," +
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
