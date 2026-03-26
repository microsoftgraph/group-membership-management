// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MembershipAggregator;
using MembershipAggregator.Activity.EmailSender;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Notifications;
using Models.ServiceBus;
using Moq;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class EmailSenderFunctionTests
    {
        private Mock<IGraphAPIService> _mockGraphAPIService;
        private EmailSenderFunction _emailSenderFunction;

        [TestInitialize]
        public void SetUp()
        {
            _mockGraphAPIService = new Mock<IGraphAPIService>();
            _emailSenderFunction = new EmailSenderFunction(NullLogger<EmailSenderFunction>.Instance, _mockGraphAPIService.Object);
        }

        [TestMethod]
        public async Task SendEmailAsyncLogsStartAndCompletionAndCallsGraphApiService()
        {

            var syncJob = new SyncJob { Group = new Group { GroupId = Guid.NewGuid() } , RunId = Guid.NewGuid(), Requestor = "test@example.com" };
      
            var emailRequest = new EmailSenderRequest
            {
                SyncJob = syncJob,
                CurrentPart = 1,
                TotalParts = 1,
                NotificationType = NotificationMessageType.NoDataNotification,
                AdditionalContentParams = new string[] { "ContentParam1", "ContentParam2" },
            };

            await _emailSenderFunction.SendEmailAsync(emailRequest);

            _mockGraphAPIService.Verify(api => api.SendEmailAsync(
                          syncJob, NotificationMessageType.NoDataNotification, It.IsAny<string[]>()),
                          Times.Once());
        }
    }
}