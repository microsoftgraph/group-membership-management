// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using DIConcreteTypes;
using Hosts.Notifier;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Kiota.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Notifications;
using Models.ThresholdNotifications;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.Localization;
using Repositories.RetryPolicyProvider;
using Repositories.ServiceBusQueue;
using Services.Contracts.Notifications;
using Services.Tests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using DIConcreteTypes;
using Repositories.Logging;
using Models.Entities;
using Repositories.Mail;
using Microsoft.Graph;
using Microsoft.Azure.Documents.SystemFunctions;
using Microsoft.Graph.Me.SendMail;
using static Microsoft.Graph.Me.SendMail.SendMailRequestBuilder;
using System.Threading;
using Microsoft.Kiota.Abstractions;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Notifier.Tests
{
    [TestClass]
    public class NotifierServiceTests
    {
        private const string GRAPH_API_V1_BASE_URL = "https://graph.microsoft.com/v1.0";
        private const string GroupMembership = "GroupMembership";

        private Mock<IGMMResources> _gmmResources;
        private Mock<IGraphGroupRepository> _graphGroupRepository;
        private Mock<IJobNotificationsRepository> _jobNotificationRepository;
        private ILocalizationRepository _localizationRepository;

        private Mock<ILoggingRepository> _loggerMock;
        private Mock<IEmailSenderRecipient> _mailAddresses;
        private Mock<IMailRepository> _mailRepository;
        private ThresholdNotification _notification;
        private Mock<INotificationRepository> _notificationRepository;
        private Mock<INotificationTypesRepository> _notificationTypesRepository;
        private NotifierService _notifierService;
        private Mock<IRequestAdapter> _requestAdapter;
        private Guid _targetOfficeGroupId;
        private TelemetryClient _telemetryClient;
        private Mock<IThresholdConfig> _thresholdConfig;
        private Mock<IThresholdNotificationService> _thresholdNotificationService;
        private List<AzureADUser> _users;
        private Mock<IServiceBusQueueRepository> _serviceBusQueueRepository;


        [TestMethod]
        public async Task CreateActionableNotificationFromContentAsync_ShouldCreateOrUpdateNotification()
        {
            SyncJob job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();

            var thresholdResult = new ThresholdResult
            {
                IncreaseThresholdPercentage = 10.0,
                DecreaseThresholdPercentage = 5.0,
                DeltaToAddCount = 5,
                DeltaToRemoveCount = 5,
                IsAdditionsThresholdExceeded = true,
                IsRemovalsThresholdExceeded = false
            };

            bool sendDisableJobNotification = true;

            var messageContent = new
            {
                ThresholdResult = thresholdResult,
                SyncJob = job,
                SendDisableJobNotification = sendDisableJobNotification
            };
            string serializedMessageContent = JsonSerializer.Serialize(messageContent);

            OrchestratorRequest request = new OrchestratorRequest
            {
                MessageType = nameof(NotificationMessageType.ThresholdNotification),
                MessageBody = serializedMessageContent
            };

            _notificationRepository.Setup(x => x.GetThresholdNotificationBySyncJobIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync((ThresholdNotification)null);

            var result = await _notifierService.CreateActionableNotificationFromContentAsync(request.MessageBody);

            Assert.IsNotNull(result);
            _notificationRepository.Verify(x => x.SaveNotificationAsync(It.IsAny<ThresholdNotification>()), Times.Once);
        }
        [TestMethod]
        public async Task SendNormalThresholdEmailAsync_SendsEmail_WithExpectedParameters()
        {
            var messageBody = "{\"SyncJob\": {\"Id\": \"12345678-1234-1234-1234-1234567890ab\", \"TargetOfficeGroupId\": \"12345678-1234-1234-1234-1234567890ab\"}, \"ThresholdResult\": {\"IncreaseThresholdPercentage\": 10.0}, \"SendDisableJobNotification\": false, \"GroupName\": \"Test Group\"}";
            await _notifierService.SendNormalThresholdEmailAsync(messageBody);
            _mailRepository.Verify(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>()), Times.Once());

        }
        [TestInitialize]
        public void SetupTest()
        {
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _mailRepository = new Mock<IMailRepository>();
            _notificationRepository = new Mock<INotificationRepository>();
            _loggerMock = new Mock<ILoggingRepository>();
            _mailAddresses = new Mock<IEmailSenderRecipient>();
            _thresholdNotificationService = new Mock<IThresholdNotificationService>();
            _users = new List<AzureADUser>();
            _notificationTypesRepository = new Mock<INotificationTypesRepository>();
            _jobNotificationRepository = new Mock<IJobNotificationsRepository>();
            _thresholdConfig = new Mock<IThresholdConfig>();
            _serviceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            _gmmResources = new Mock<IGMMResources>();
            _notification = new ThresholdNotification
            {
                Id = Guid.NewGuid(),
                ChangePercentageForAdditions = 1,
                ChangePercentageForRemovals = 1,
                CreatedTime = DateTime.UtcNow,
                Resolution = ThresholdNotificationResolution.Unresolved,
                ResolvedByUPN = string.Empty,
                ResolvedTime = DateTime.UtcNow,
                Status = ThresholdNotificationStatus.Unknown,
                CardState = ThresholdNotificationCardState.DefaultCard,
                TargetOfficeGroupId = _targetOfficeGroupId,
                ThresholdPercentageForAdditions = -1,
                ThresholdPercentageForRemovals = -1,
            };
            _loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));
            _telemetryClient = new TelemetryClient(new TelemetryConfiguration());

            for (int i = 0; i < 2; i++)
            {
                var user = new AzureADUser
                {
                    Mail = $"owner_{i}@email.com"
                };
                _users.Add(user);
            }
            _graphGroupRepository.Setup(x => x.GetGroupOwnersAsync(_targetOfficeGroupId, 0)).Returns(() => Task.FromResult(_users));
            _graphGroupRepository.Setup(x => x.GetGroupNameAsync(It.Is<Guid>(id => id == _targetOfficeGroupId))).ReturnsAsync($"Test Group with id {_targetOfficeGroupId}");
            _thresholdNotificationService.Setup(x => x.CreateNotificationCardAsync(It.IsAny<ThresholdNotification>())).ReturnsAsync(_notification.Id.ToString());

            var options = Options.Create(new LocalizationOptions { ResourcesPath = "Resources" });
            var factory = new ResourceManagerStringLocalizerFactory(options, NullLoggerFactory.Instance);
            var localizer = new StringLocalizer<LocalizationRepository>(factory);
            _localizationRepository = new LocalizationRepository(localizer);

            _notifierService = new NotifierService(_loggerMock.Object,
                                                _mailRepository.Object,
                                                _mailAddresses.Object,
                                                _localizationRepository,
                                                _thresholdNotificationService.Object,
                                                _notificationRepository.Object,
                                                _graphGroupRepository.Object,
                                                _notificationTypesRepository.Object,
                                                _jobNotificationRepository.Object,
                                                _thresholdConfig.Object,
                                                _gmmResources.Object,
                                                _serviceBusQueueRepository.Object,
                                                _telemetryClient
                                                );
            _requestAdapter = new Mock<IRequestAdapter>();
            _requestAdapter.SetupProperty(x => x.BaseUrl).SetReturnsDefault(GRAPH_API_V1_BASE_URL);

            string requestUrl = null;
            HttpMethod requestMethod = null;
            _requestAdapter.Setup(x => x.ConvertToNativeRequestAsync<HttpRequestMessage>(It.IsAny<RequestInformation>(), It.IsAny<CancellationToken>()))
                .Callback<object, object>((r, t) =>
                {
                    var request = r as RequestInformation;
                    requestUrl = request.URI.ToString();
                    requestMethod = new HttpMethod(request.HttpMethod.ToString());
                })
               .ReturnsAsync(() => new HttpRequestMessage(HttpMethod.Get, requestUrl));
        }

        [TestMethod]
        public async Task TestRetrieveQueuedNotifications()
        {
            await _notifierService.RetrieveQueuedNotificationsAsync();
            _notificationRepository.Verify(x => x.GetQueuedNotificationsAsync(), Times.Once());
        }

        [TestMethod]
        public async Task TestSendEmailAsync()
        {
            SyncJob job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            var notificationTypeId = 1;

            string[] additionalContentParameters = new string[] { "Param1", "Param2" };
            var messageContent = new Dictionary<string, Object>
            {
                { "SyncJob", job },
                { "AdditionalContentParameters", additionalContentParameters }
            };
            string subjectTemplate = "DisabledJobEmailSubject";
            string contentTemplate = "SyncDisabledNoGroupEmailBody";
            _notificationTypesRepository.Setup(repo => repo.GetNotificationTypeByNotificationTypeNameAsync(contentTemplate))
                .ReturnsAsync(new NotificationType { Id = notificationTypeId, Name = contentTemplate, Disabled = false });

            _jobNotificationRepository.Setup(repo => repo.IsNotificationDisabledForJobAsync(job.Id, notificationTypeId))
                .ReturnsAsync(false);

            string serializedMessageContent = JsonSerializer.Serialize(messageContent);
            OrchestratorRequest request = new OrchestratorRequest
            {
                MessageType = nameof(NotificationMessageType.SyncStartedNotification),
                MessageBody = serializedMessageContent,
                SubjectTemplate = subjectTemplate,
                ContentTemplate = contentTemplate
            };

            await _notifierService.SendEmailAsync(request.MessageType, request.MessageBody, request.SubjectTemplate, request.ContentTemplate);
            _mailRepository.Verify(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>()), Times.Once());
        }
        [TestMethod]
        public async Task TestSendEmailServiceUnavailableAsync()

        {
            var retryRepo = new RetryPolicyProvider(_loggerMock.Object, new GraphServiceAttemptsValue { MaxExceptionHandlingAttempts = 2, MaxRetryAfterAttempts = 4 });
            var runId = Guid.NewGuid();
            var retryAfterPolicy = retryRepo.CreateRetryAfterPolicy(runId);
            var exceptionHandlingPolicy = retryRepo.CreateExceptionHandlingPolicy(runId);
            var policy = retryAfterPolicy.WrapAsync(exceptionHandlingPolicy);
            var retryAttempt = 0;
            var testLimit = 10;
            var invalidCode = HttpStatusCode.ServiceUnavailable;
            var validCode = HttpStatusCode.OK;

            var response = await policy.ExecuteAsync(async () =>
            {
                return await SendMailRetryAsync(retryAttempt++, testLimit, invalidCode, validCode);
            });
            Assert.AreEqual(invalidCode, response.StatusCode);
            Assert.AreEqual(3, retryAttempt);
        }
        [TestMethod]
        public async Task TestSendEmailTooManyRequestsAsync()

        {
            var retryRepo = new RetryPolicyProvider(_loggerMock.Object, new GraphServiceAttemptsValue { MaxExceptionHandlingAttempts = 2, MaxRetryAfterAttempts = 4 });
            var runId = Guid.NewGuid();
            var retryAfterPolicy = retryRepo.CreateRetryAfterPolicy(runId);
            var exceptionHandlingPolicy = retryRepo.CreateExceptionHandlingPolicy(runId);
            var policy = retryAfterPolicy.WrapAsync(exceptionHandlingPolicy);
            var retryAttempt = 0;
            var testLimit = 10;
            var invalidCode = HttpStatusCode.TooManyRequests;
            var validCode = HttpStatusCode.OK;

            var response = await policy.ExecuteAsync(async () =>
            {
                return await SendMailRetryAsync(retryAttempt++, testLimit, invalidCode, validCode);
            });
            Assert.AreEqual(invalidCode, response.StatusCode);
            Assert.AreEqual(5, retryAttempt);
        }
        [TestMethod]
        public async Task TestSendEmailRetryAsync()

        {
            var retryRepo = new RetryPolicyProvider(_loggerMock.Object, new GraphServiceAttemptsValue { MaxExceptionHandlingAttempts = 2, MaxRetryAfterAttempts = 4 });
            var runId = Guid.NewGuid();
            var retryAfterPolicy = retryRepo.CreateRetryAfterPolicy(runId);
            var exceptionHandlingPolicy = retryRepo.CreateExceptionHandlingPolicy(runId);
            var policy = retryAfterPolicy.WrapAsync(exceptionHandlingPolicy);
            var retryAttempt = 0;
            var testLimit = 2;
            var invalidCode = HttpStatusCode.ServiceUnavailable;
            var validCode = HttpStatusCode.OK;

            var response = await policy.ExecuteAsync(async () =>
            {
                return await SendMailRetryAsync(retryAttempt++, testLimit, invalidCode, validCode);
            });
            Assert.AreEqual(validCode, response.StatusCode);
            Assert.AreEqual(3, retryAttempt);
        }
        private async Task<HttpResponseMessage> SendMailRetryAsync(int retryAttempt, int testLimit, HttpStatusCode invalidCode, HttpStatusCode validCode)
        {
            var httpStatusCode = (retryAttempt < testLimit) ? invalidCode : validCode;
            var response = new HttpResponseMessage(httpStatusCode);

            if (httpStatusCode == HttpStatusCode.TooManyRequests)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTime.UtcNow);
            }
            return await Task.FromResult(response);
        }
        [TestMethod]
        public async Task TestSendThresholdEmail()
        {
            await _notifierService.SendThresholdEmailAsync(_notification);
            _mailRepository.Verify(x => x.SendMailAsync(It.IsAny<EmailMessage>(), null), Times.Once());
        }

        [TestMethod]
        public async Task TestUpdateNotificationStatus()
        {
            await _notifierService.UpdateNotificationStatusAsync(_notification, ThresholdNotificationStatus.Queued);
            _notificationRepository.Verify(x => x.UpdateNotificationStatusAsync(It.IsAny<ThresholdNotification>(), It.IsAny<ThresholdNotificationStatus>()), Times.Once());
        }
        [TestMethod]
        public async Task VerifyEmailNotSentIfDisabled()
        {
            var job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            var notificationTypeId = 1;
            var notificationName = "SyncStartedEmailBody";

            _notificationTypesRepository.Setup(repo => repo.GetNotificationTypeByNotificationTypeNameAsync(notificationName))
                .ReturnsAsync(new NotificationType { Id = notificationTypeId, Name = notificationName, Disabled = false });

            _jobNotificationRepository.Setup(repo => repo.IsNotificationDisabledForJobAsync(job.Id, notificationTypeId))
                .ReturnsAsync(true);

            bool result = await _notifierService.IsNotificationDisabledAsync(job.Id, notificationName);
            Assert.IsTrue(result);

        }

        [TestMethod]
        public async Task VerifyEmailNotSentIfGloballyDisabled()
        {
            var job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            var notificationTypeId = 1;
            var notificationName = "SyncStartedEmailBody";

            _notificationTypesRepository.Setup(repo => repo.GetNotificationTypeByNotificationTypeNameAsync(notificationName))
                .ReturnsAsync(new NotificationType { Id = notificationTypeId, Name = notificationName, Disabled = true });

            _jobNotificationRepository.Setup(repo => repo.IsNotificationDisabledForJobAsync(job.Id, notificationTypeId))
                .ReturnsAsync(false);

            bool result = await _notifierService.IsNotificationDisabledAsync(job.Id, notificationName);
            Assert.IsTrue(result);

        }
        [TestMethod]
        public async Task SendEmailAsync_ShouldHandleNonOKResponse()
        {
            var job = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid(), Requestor = "requestor@example.com", TargetOfficeGroupId = Guid.NewGuid() };
            var messageBody = JsonSerializer.Serialize(new { SyncJob = job });
            var messageType = "TestMessageType";
            var subjectTemplate = "TestSubjectTemplate";
            var contentTemplate = "SyncDisabledNoGroupEmailBody";

            var responseMessage = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                ReasonPhrase = "Too Many Requests"
            };

            _mailRepository.Setup(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>()))
                .ReturnsAsync(responseMessage);
            await _notifierService.SendEmailAsync(messageType, messageBody, subjectTemplate, contentTemplate);

            _mailRepository.Verify(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>()), Times.Once);

            var messageContent = new Dictionary<string, Object>
            {
                { "MessageBody", messageBody },
                { "MessageType", messageType },
                { "HttpStatusCode", responseMessage.StatusCode.ToString() },
                { "ReasonPhrase", responseMessage.ReasonPhrase.ToString() }
            };
            var body = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(messageContent));
            var failedMessage = new Models.ServiceBus.ServiceBusMessage
            {
                MessageId = $"{job.Id}_{job.RunId}_{messageType}",
                Body = body
            };

            _serviceBusQueueRepository.Verify(x => x.SendMessageAsync(It.Is<Models.ServiceBus.ServiceBusMessage>(m =>
                m.MessageId == failedMessage.MessageId &&
                m.Body.SequenceEqual(failedMessage.Body)
            )), Times.Once);
        }

        [TestMethod]
        public async Task SendEmailWhenEmailsHaveBeenDisabled()
        {

            var requestAdapter = new Mock<IRequestAdapter>();
            requestAdapter.SetupProperty(x => x.BaseUrl).SetReturnsDefault("https://graph.microsoft.com/v1.0");
            var graphServiceClient = new Mock<GraphServiceClient>(requestAdapter.Object, "https://graph.microsoft.com/v1.0");
            var retryRepo = new RetryPolicyProvider(_loggerMock.Object, new GraphServiceAttemptsValue { MaxExceptionHandlingAttempts = 2, MaxRetryAfterAttempts = 4 });

            var mailConfig = new MailConfig(true, false, "not-set", true);
            var mailRepository = new MailRepository(graphServiceClient.Object,
                                                    mailConfig,
                                                    _localizationRepository,
                                                    _loggerMock.Object,
                                                    "abc",
                                                    _graphGroupRepository.Object,
                                                    new Mock<IDatabaseSettingsRepository>().Object,
                                                    retryRepo);

            _notifierService = new NotifierService(_loggerMock.Object,
                                    mailRepository,
                                    _mailAddresses.Object,
                                    _localizationRepository,
                                    _thresholdNotificationService.Object,
                                    _notificationRepository.Object,
                                    _graphGroupRepository.Object,
                                    _notificationTypesRepository.Object,
                                    _jobNotificationRepository.Object,
                                    _thresholdConfig.Object,
                                    _gmmResources.Object,
                                    _serviceBusQueueRepository.Object,
                                    _telemetryClient
                                    );

            SyncJob job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            var notificationTypeId = 1;

            string[] additionalContentParameters = new string[] { "Param1", "Param2" };
            var messageContent = new Dictionary<string, Object>
            {
                { "SyncJob", job },
                { "AdditionalContentParameters", additionalContentParameters }
            };
            string subjectTemplate = "DisabledJobEmailSubject";
            string contentTemplate = "SyncDisabledNoGroupEmailBody";
            _notificationTypesRepository.Setup(repo => repo.GetNotificationTypeByNotificationTypeNameAsync(contentTemplate))
                .ReturnsAsync(new NotificationType { Id = notificationTypeId, Name = contentTemplate, Disabled = false });

            _jobNotificationRepository.Setup(repo => repo.IsNotificationDisabledForJobAsync(job.Id, notificationTypeId))
                .ReturnsAsync(false);

            string serializedMessageContent = JsonSerializer.Serialize(messageContent);
            OrchestratorRequest request = new OrchestratorRequest
            {
                MessageType = nameof(NotificationMessageType.SyncStartedNotification),
                MessageBody = serializedMessageContent,
                SubjectTemplate = subjectTemplate,
                ContentTemplate = contentTemplate
            };

            await _notifierService.SendEmailAsync(request.MessageType, request.MessageBody, request.SubjectTemplate, request.ContentTemplate);
            _loggerMock.Verify(x => x.LogMessageAsync(It.Is<LogMessage>(m => m.Message.Equals("Email notifications are disabled.")), 
                                                        VerbosityLevel.INFO, 
                                                        It.IsAny<string>(), 
                                                        It.IsAny<string>()), Times.Once());
        }
    }
}
