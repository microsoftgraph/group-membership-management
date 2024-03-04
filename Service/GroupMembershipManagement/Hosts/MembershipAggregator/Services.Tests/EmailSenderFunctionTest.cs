// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MembershipAggregator;
using MembershipAggregator.Activity.EmailSender;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Notifications;
using Models.ServiceBus;
using Moq;
using Polly;
using Repositories.Contracts;
using Repositories.Logging;
using Repositories.ServiceBusQueue;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class EmailSenderFunctionTests
    {
        private Mock<ILoggingRepository> _mockLoggingRepository;
        private Mock<IGraphAPIService> _mockGraphAPIService;
        private EmailSenderFunction _emailSenderFunction;
        private Mock<IServiceBusQueueRepository> _serviceBusQueueRepository;

        [TestInitialize]
        public void SetUp()
        {
            _mockLoggingRepository = new Mock<ILoggingRepository>();
            _mockGraphAPIService = new Mock<IGraphAPIService>();
            _emailSenderFunction = new EmailSenderFunction(_mockLoggingRepository.Object, _mockGraphAPIService.Object);
            _serviceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
        }

        [TestMethod]
        public async Task SendEmailAsyncLogsStartAndCompletionAndCallsGraphApiService()
        {

            var syncJob = new SyncJob { TargetOfficeGroupId = Guid.NewGuid(), RunId = Guid.NewGuid(), Requestor = "test@example.com" };
      
            var emailRequest = new EmailSenderRequest
            {
                SyncJob = syncJob,
                NotificationType = NotificationMessageType.NoDataNotification,
                AdditionalContentParams = new string[] { "ContentParam1", "ContentParam2" },
            };

            await _emailSenderFunction.SendEmailAsync(emailRequest);

            _mockLoggingRepository.Verify(log => log.LogMessageAsync(
                It.Is<LogMessage>(m => m.Message.Contains("EmailSenderFunction function started")), 
                VerbosityLevel.DEBUG, 
                It.IsAny<string>(), 
                It.IsAny<string>()), 
                Times.Once());

            _mockLoggingRepository.Verify(log => log.LogMessageAsync(
                It.Is<LogMessage>(m => m.Message.Contains("EmailSenderFunction function completed")), 
                VerbosityLevel.DEBUG, 
                It.IsAny<string>(), 
                It.IsAny<string>()), 
                Times.Once());

            _mockGraphAPIService.Verify(api => api.SendEmailAsync(
                          syncJob, NotificationMessageType.NoDataNotification, It.IsAny<string[]>()),
                          Times.Once());
        }
    }
}