// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using DIConcreteTypes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.Mocks;
using Repositories.ServiceBusTopics.Tests;
using Services.Contracts;
using System;
using System.Data.SqlTypes;
using System.Linq;
using System.Threading.Tasks;
using Tests.Repositories;
using MockDatabaseSyncJobRepository = Repositories.Mocks.MockDatabaseSyncJobRepository;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using System.Collections.Generic;
using Repositories.Logging;

namespace Services.Tests
{
    [TestClass]
    public class JobTriggerServiceTests
    {
        private JobTriggerService _jobTriggerService = null;
        private MockDatabaseSyncJobRepository _syncJobRepository = null;
        private MockNotificationTypesRepository _notificationTypesRepository = null;
        private MockDisabledJobNotificationRepository _disabledJobNotificationRepository = null;
        private MockLoggingRepository _loggingRepository = null;
        private MockServiceBusTopicsRepository _serviceBusTopicsRepository = null;
        private MockGraphGroupRepository _graphGroupRepository;
        private MockMailRepository _mailRepository = null;
        private GMMResources _gMMResources = null;
        private MockJobTriggerConfig _jobTriggerConfig = null;

		private const string Organization = "Organization";
        private const string GroupMembership = "GroupMembership";
        private const string EmailSubject = "EmailSubject";
        private const string SyncStartedEmailBody = "SyncStartedEmailBody";
        private const string SyncDisabledNoGroupEmailBody = "SyncDisabledNoGroupEmailBody";

        [TestInitialize]
        public void InitializeTest()
        {
            _gMMResources = new GMMResources
            {
                LearnMoreAboutGMMUrl = "http://learn-more-about-gmm"
            };

            _syncJobRepository = new MockDatabaseSyncJobRepository();
            _notificationTypesRepository = new MockNotificationTypesRepository();
            _disabledJobNotificationRepository = new MockDisabledJobNotificationRepository();
            _loggingRepository = new MockLoggingRepository();
            _serviceBusTopicsRepository = new MockServiceBusTopicsRepository();
            _graphGroupRepository = new MockGraphGroupRepository();
            _mailRepository = new MockMailRepository();
            _jobTriggerConfig = new MockJobTriggerConfig();
            _jobTriggerService = new JobTriggerService(
                                        _loggingRepository,
                                        _syncJobRepository,
                                        _notificationTypesRepository,
                                        _disabledJobNotificationRepository,
                                        _serviceBusTopicsRepository,
                                        _graphGroupRepository,
                                        new MockKeyVaultSecret<IJobTriggerService>(), _mailRepository,
                                        new MockEmail<IEmailSenderRecipient>(),
                                        _gMMResources,
                                        _jobTriggerConfig,
                                        new TelemetryClient(TelemetryConfiguration.CreateDefault()));
        }

        public Guid getDestinationObjectId(SyncJob job)
        {
            return new Guid((JArray.Parse(job.Destination)[0] as JObject)["value"]["objectId"].Value<string>());
        }

        [TestMethod]
        public async Task TestValidGroupDestinationQuery()
        {
            SyncJob job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            var objectId = Guid.NewGuid();
            job.Destination = $"[{{\"type\":\"GroupMembership\",\"value\":{{\"objectId\":\"{objectId}\"}}}}]";

            var parsedAndValidated = await _jobTriggerService.ParseAndValidateDestinationAsync(job);

            Assert.AreEqual(true, parsedAndValidated.IsValid);
            Assert.AreEqual(objectId, parsedAndValidated.DestinationObject.Value.ObjectId);
        }

        [TestMethod]
        public async Task TestValidTeamsDestinationQuery()
        {
            SyncJob job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            var objectId = Guid.NewGuid();
            var channelId = "Channel_ID";
            job.Destination = $"[{{\"type\":\"TeamsChannelMembership\",\"value\":{{\"objectId\":\"{objectId}\",\"channelId\":\"{channelId}\"}}}}]";

            var parsedAndValidated = await _jobTriggerService.ParseAndValidateDestinationAsync(job);

            Assert.AreEqual(true, parsedAndValidated.IsValid);
            Assert.AreEqual(objectId, parsedAndValidated.DestinationObject.Value.ObjectId);
            Assert.AreEqual(channelId, (parsedAndValidated.DestinationObject.Value as TeamsChannelDestinationValue).ChannelId);
        }

        [TestMethod]
        public async Task TestEmptyDestinationQuery()
        {
            SyncJob job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            job.Destination = "";

            var parsedAndValidated = await _jobTriggerService.ParseAndValidateDestinationAsync(job);

            Assert.AreEqual(false, parsedAndValidated.IsValid);
            Assert.AreEqual(null, parsedAndValidated.DestinationObject);
        }

        [TestMethod]
        public async Task TestInvalidDestinationQueryDueToMissingType()
        {
            SyncJob job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            job.Destination = $"[{{\"value\":{{\"objectId\":\"{Guid.NewGuid()}\"}}}}]";

            var parsedAndValidated = await _jobTriggerService.ParseAndValidateDestinationAsync(job);

            Assert.AreEqual(false, parsedAndValidated.IsValid);
            Assert.AreEqual(null, parsedAndValidated.DestinationObject);
        }

        [TestMethod]
        public async Task TestInvalidTeamsDestinationQuery()
        {
            SyncJob job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            job.Destination = $"[{{\"type\":\"TeamsChannelMembership\",\"value\":{{\"objectId\":\"{Guid.NewGuid()}\"}}}}]";

            var parsedAndValidated = await _jobTriggerService.ParseAndValidateDestinationAsync(job);

            Assert.AreEqual(false, parsedAndValidated.IsValid);
            Assert.AreEqual(null, parsedAndValidated.DestinationObject);
        }

        [TestMethod]
        public async Task ValidateJobsAreAddedToCorrectSubscription()
        {
            var organizationJobCount = 5;
            var groupMembershipJobCount = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(organizationJobCount, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(groupMembershipJobCount, GroupMembership));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(getDestinationObjectId(x)));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(getDestinationObjectId(x)));

            foreach (var job in _syncJobRepository.Jobs)
            {
                await _jobTriggerService.SendMessageAsync(job);
            }
            Assert.AreEqual(organizationJobCount, _serviceBusTopicsRepository.Subscriptions[Organization].Count);
            Assert.AreEqual(groupMembershipJobCount, _serviceBusTopicsRepository.Subscriptions[GroupMembership].Count);
        }

        [TestMethod]
        public async Task VerifyJobsWithNonexistentTargetGroupsAreErrored()
        {
            var enabledJobs = 5;
            var disabledJobs = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(enabledJobs, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(disabledJobs, Organization));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(getDestinationObjectId(x)));

            foreach (var job in _syncJobRepository.Jobs.Take(enabledJobs))
            {
                var response = await _jobTriggerService.GroupExistsAndGMMCanWriteToGroupAsync(job);
                Assert.AreEqual(false, response);
            }
        }

        [TestMethod]
        public async Task VerifyJobsGMMDoesntOwnAreErrored()
        {
            var enabledJobs = 5;
            var disabledJobs = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(enabledJobs, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(disabledJobs, Organization));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(getDestinationObjectId(x)));

            foreach (var job in _syncJobRepository.Jobs.Take(enabledJobs))
            {
                var response = await _jobTriggerService.GroupExistsAndGMMCanWriteToGroupAsync(job);
                Assert.AreEqual(false, response);
            }
        }

        [TestMethod]
        public async Task VerifyJobsWithValidStartDateAreProcessed()
        {
            var validStartDateJobs = 5;
            var futureStartDateJobs = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(validStartDateJobs, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(futureStartDateJobs, Organization, startDateBase: DateTime.UtcNow.AddDays(5)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(getDestinationObjectId(x)));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(getDestinationObjectId(x)));

            var response = await _jobTriggerService.GetSyncJobsAsync();
			var jobs = response.jobs;

			var jobsToProcessCount = _serviceBusTopicsRepository.Subscriptions.Sum(x => x.Value.Count);

            Assert.AreEqual(validStartDateJobs, jobs.Count);
        }

        [TestMethod]
        public async Task VerifyJobsWithValidPeriodsAreProcessed()
        {
            var jobsWithValidPeriods = 5;
            var jobsWithInvalidPeriods = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(jobsWithValidPeriods, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(jobsWithInvalidPeriods, Organization, lastRunTime: DateTime.UtcNow.AddMinutes(30)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(getDestinationObjectId(x)));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(getDestinationObjectId(x)));

            var response = await _jobTriggerService.GetSyncJobsAsync();
			var jobs = response.jobs;

			var jobsToProcessCount = _serviceBusTopicsRepository.Subscriptions.Sum(x => x.Value.Count);

            Assert.AreEqual(jobsWithValidPeriods, jobs.Count);
        }

        [TestMethod]
        public async Task VerifyJobStatusIsUpdatedToInProgress()
        {
            var jobs = 2;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(jobs, Organization));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(getDestinationObjectId(x)));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(getDestinationObjectId(x)));
      
            foreach (var job in _syncJobRepository.Jobs)
            {                
                var canWriteToGroup = await _jobTriggerService.GroupExistsAndGMMCanWriteToGroupAsync(job);
                await _jobTriggerService.UpdateSyncJobStatusAsync(canWriteToGroup ? SyncStatus.InProgress : SyncStatus.NotOwnerOfDestinationGroup, job);
                Assert.AreEqual(job.Status, SyncStatus.InProgress.ToString());
            }
        }

        [TestMethod]
        public async Task VerifyJobStatusIsUpdatedToStuckInProgress()
        {
            var jobs = 2;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(jobs, Organization));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(getDestinationObjectId(x)));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(getDestinationObjectId(x)));
           
            foreach (var job in _syncJobRepository.Jobs)
            {
                job.Status = SyncStatus.InProgress.ToString();
                var canWriteToGroup = await _jobTriggerService.GroupExistsAndGMMCanWriteToGroupAsync(job);
                await _jobTriggerService.UpdateSyncJobStatusAsync(canWriteToGroup ? SyncStatus.StuckInProgress : SyncStatus.NotOwnerOfDestinationGroup, job);
                Assert.AreEqual(job.Status, SyncStatus.StuckInProgress.ToString());
            }
        }

        [TestMethod]
        public async Task VerifyJobStatusIsUpdatedToErrorDueToNoGroupOwnership()
        {
            var jobs = 2;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(jobs, Organization));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(getDestinationObjectId(x)));

            foreach (var job in _syncJobRepository.Jobs)
            {
                var canWriteToGroup = await _jobTriggerService.GroupExistsAndGMMCanWriteToGroupAsync(job);
                await _jobTriggerService.UpdateSyncJobStatusAsync(canWriteToGroup ? SyncStatus.InProgress : SyncStatus.NotOwnerOfDestinationGroup, job);
                Assert.AreEqual(job.Status, SyncStatus.NotOwnerOfDestinationGroup.ToString());
            }
        }


        [TestMethod]
        public async Task VerifyJobStatusIsUpdatedToInProgressDueToTenantAPIPermissions()
        {
            var jobs = 2;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(jobs, Organization));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(getDestinationObjectId(x)));

            _jobTriggerConfig.GMMHasGroupReadWriteAllPermissions = true;

            foreach (var job in _syncJobRepository.Jobs)
            {
                var canWriteToGroup = await _jobTriggerService.GroupExistsAndGMMCanWriteToGroupAsync(job);
                await _jobTriggerService.UpdateSyncJobStatusAsync(canWriteToGroup ? SyncStatus.InProgress : SyncStatus.NotOwnerOfDestinationGroup, job);
                Assert.AreEqual(job.Status, SyncStatus.InProgress.ToString());
            }
        }


        [TestMethod]
        public async Task VerifyUniqueMessageIdsAreCreated()
        {
            var MessageIdOne = "";
            var MessageIdTwo = "";

            var groupMembershipJobCount = 1;
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(groupMembershipJobCount, GroupMembership));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(getDestinationObjectId(x)));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(getDestinationObjectId(x)));

            await _jobTriggerService.SendMessageAsync(_syncJobRepository.Jobs[0]);

            foreach (var job in _syncJobRepository.Jobs)
            {
                job.RunId = Guid.NewGuid();
                var message = _serviceBusTopicsRepository.CreateMessage(job);
                MessageIdOne = message.MessageId;
                job.Status = SyncStatus.Idle.ToString();
            }

            await _jobTriggerService.SendMessageAsync(_syncJobRepository.Jobs[0]);

            foreach (var job in _syncJobRepository.Jobs)
            {
                job.RunId = Guid.NewGuid();
                var message = _serviceBusTopicsRepository.CreateMessage(job);
                MessageIdTwo = message.MessageId;
            }
            Assert.AreNotEqual(MessageIdOne, MessageIdTwo);

        }

        [TestMethod]
        public async Task VerifyInitialSyncEmailNotificationIsSent()
        {
            var _mailRepository = new Mock<IMailRepository>();
            _mailRepository.Setup(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>(), ""));

            _jobTriggerService = new JobTriggerService(
                _loggingRepository,
                _syncJobRepository,
                _notificationTypesRepository,
                _disabledJobNotificationRepository,
                _serviceBusTopicsRepository,
                _graphGroupRepository,
                new MockKeyVaultSecret<IJobTriggerService>(),
                _mailRepository.Object,
                new MockEmail<IEmailSenderRecipient>(),
                _gMMResources,
                _jobTriggerConfig,
				new TelemetryClient(TelemetryConfiguration.CreateDefault()));

			var validStartDateJobs = 5;
            var futureStartDateJobs = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(validStartDateJobs, Organization, lastRunTime: SqlDateTime.MinValue.Value));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(futureStartDateJobs, Organization, startDateBase: DateTime.UtcNow.AddDays(5), lastRunTime: SqlDateTime.MinValue.Value));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(getDestinationObjectId(x)));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(getDestinationObjectId(x)));

            var response = await _jobTriggerService.GetSyncJobsAsync();
            var jobs = response.jobs;

            foreach (var job in jobs)
            {
                _jobTriggerService.RunId = job.RunId.Value;
                var groupName = await _graphGroupRepository.GetGroupNameAsync(getDestinationObjectId(job));
                await _jobTriggerService.SendEmailAsync(job, EmailSubject, SyncStartedEmailBody, new string[] { });

                Assert.IsNotNull(_jobTriggerService.RunId);
                Assert.IsNotNull(_graphGroupRepository.RunId);
                Assert.AreEqual(_jobTriggerService.RunId, _graphGroupRepository.RunId);
            }

            Assert.AreEqual(validStartDateJobs, jobs.Count);
            _mailRepository.Verify(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>(), ""), Times.Exactly(validStartDateJobs));
        }

        [TestMethod]
        public async Task VerifyJobsAreProcessedWithMissingMailSendPermission()
        {
            var _mailRepository = new Mock<IMailRepository>();
            _mailRepository.Setup(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>(), ""));

            _jobTriggerService = new JobTriggerService(
                _loggingRepository,
                _syncJobRepository,
                _notificationTypesRepository,
				_disabledJobNotificationRepository,
				_serviceBusTopicsRepository,
                _graphGroupRepository,
                new MockKeyVaultSecret<IJobTriggerService>(),
                _mailRepository.Object,
                new MockEmail<IEmailSenderRecipient>(),
                _gMMResources,
                _jobTriggerConfig,
				new TelemetryClient(TelemetryConfiguration.CreateDefault()));

			var validStartDateJobs = 5;
            var futureStartDateJobs = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(validStartDateJobs, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(futureStartDateJobs, Organization, startDateBase: DateTime.UtcNow.AddDays(5)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(getDestinationObjectId(x)));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(getDestinationObjectId(x)));

            var response = await _jobTriggerService.GetSyncJobsAsync();
			var jobs = response.jobs;

			foreach (var job in jobs)
            {
                var groupName = await _graphGroupRepository.GetGroupNameAsync(getDestinationObjectId(job));
                await _jobTriggerService.SendEmailAsync(job, EmailSubject, SyncStartedEmailBody, new string[] { });
            }

            Assert.AreEqual(validStartDateJobs, jobs.Count);
        }

        [TestMethod]
        public async Task VerifyJobsAreProcessedWithMissingMailLicenses()
        {
            var _mailRepository = new Mock<IMailRepository>();
            _mailRepository.Setup(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>(), ""));

            _jobTriggerService = new JobTriggerService(
                _loggingRepository,
                _syncJobRepository,
				_notificationTypesRepository,
				_disabledJobNotificationRepository,
				_serviceBusTopicsRepository,
                _graphGroupRepository,
                new MockKeyVaultSecret<IJobTriggerService>(),
                _mailRepository.Object,
                new MockEmail<IEmailSenderRecipient>(),
                _gMMResources,
                _jobTriggerConfig,
				new TelemetryClient(TelemetryConfiguration.CreateDefault()));

			var validStartDateJobs = 5;
            var futureStartDateJobs = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(validStartDateJobs, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(futureStartDateJobs, Organization, startDateBase: DateTime.UtcNow.AddDays(5)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(getDestinationObjectId(x)));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(getDestinationObjectId(x)));

            var response = await _jobTriggerService.GetSyncJobsAsync();
			var jobs = response.jobs;

			foreach (var job in jobs)
            {
                await _jobTriggerService.SendEmailAsync(job, EmailSubject, SyncStartedEmailBody, new string[] { });
            }

            Assert.AreEqual(validStartDateJobs, jobs.Count);
        }

        [TestMethod]
        public async Task VerifyJobsAreProcessedMailingExceptions()
        {
            var _mailRepository = new Mock<IMailRepository>();
            _mailRepository.Setup(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>(), ""));

            _jobTriggerService = new JobTriggerService(
                _loggingRepository,
                _syncJobRepository,
			    _notificationTypesRepository,
				_disabledJobNotificationRepository,
				_serviceBusTopicsRepository,
                _graphGroupRepository,
                new MockKeyVaultSecret<IJobTriggerService>(),
                _mailRepository.Object,
                new MockEmail<IEmailSenderRecipient>(),
                _gMMResources,
                _jobTriggerConfig,
				new TelemetryClient(TelemetryConfiguration.CreateDefault()));

			var validStartDateJobs = 5;
            var futureStartDateJobs = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(validStartDateJobs, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(futureStartDateJobs, Organization, startDateBase: DateTime.UtcNow.AddDays(5)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(getDestinationObjectId(x)));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(getDestinationObjectId(x)));

            var response = await _jobTriggerService.GetSyncJobsAsync();
			var jobs = response.jobs;

			foreach (var job in jobs)
            {
                await _jobTriggerService.SendEmailAsync(job, EmailSubject, SyncStartedEmailBody, new string[] { });
            }

            Assert.AreEqual(validStartDateJobs, jobs.Count);
        }

		[TestMethod]
		public async Task VerifyEmailNotSentIfDisabled()
		{
			var _mailRepository = new Mock<IMailRepository>();

			_mailRepository.Setup(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>(), ""));
			SyncJob job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
			var notificationName = SyncStartedEmailBody;
			var notificationTypeId = 1;
			var mockNotificationTypesData = new Dictionary<string, NotificationType>
            {
	            { notificationName, new NotificationType { Id = notificationTypeId, Disabled = false } }
            };
			var _notificationTypesRepository = new MockNotificationTypesRepository(mockNotificationTypesData);

			var jobId = job.Id;
			var mockDisabledJobNotificationData = new Dictionary<(Guid, int), bool>
	        {
		        { (jobId, notificationTypeId), true }
	        };
			var _disabledJobNotificationRepository = new MockDisabledJobNotificationRepository(mockDisabledJobNotificationData);

			_jobTriggerService = new JobTriggerService(
				_loggingRepository,
				_syncJobRepository,
				_notificationTypesRepository,
				_disabledJobNotificationRepository,
				_serviceBusTopicsRepository,
				_graphGroupRepository,
				new MockKeyVaultSecret<IJobTriggerService>(),
				_mailRepository.Object,
				new MockEmail<IEmailSenderRecipient>(),
				_gMMResources,
				_jobTriggerConfig,
				new TelemetryClient(TelemetryConfiguration.CreateDefault()));

			await _jobTriggerService.SendEmailAsync(job, EmailSubject, SyncStartedEmailBody, new string[] { });
			var expectedLogMessage = $"Notification template '{SyncStartedEmailBody}' is disabled for job {job.Id} with destination group {job.TargetOfficeGroupId}.";
			_mailRepository.Verify(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>(), ""), Times.Never());
			Assert.AreEqual(1, _loggingRepository.MessagesLoggedCount);
			Assert.AreEqual(expectedLogMessage, _loggingRepository.MessagesLogged[0].Message);
		}

		[TestMethod]
		public async Task VerifyEmailNotSentIfGloballyDisabled()
		{
			var _mailRepository = new Mock<IMailRepository>();

			_mailRepository.Setup(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>(), ""));
			SyncJob job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
			var notificationName = SyncStartedEmailBody;
			var notificationTypeId = 1;
			var mockNotificationTypesData = new Dictionary<string, NotificationType>
			{
				{ notificationName, new NotificationType { Id = notificationTypeId, Disabled = true } }
			};
			var _notificationTypesRepository = new MockNotificationTypesRepository(mockNotificationTypesData);

			var jobId = job.Id;

			var _disabledJobNotificationRepository = new MockDisabledJobNotificationRepository();

			_jobTriggerService = new JobTriggerService(
				_loggingRepository,
				_syncJobRepository,
				_notificationTypesRepository,
				_disabledJobNotificationRepository,
				_serviceBusTopicsRepository,
				_graphGroupRepository,
				new MockKeyVaultSecret<IJobTriggerService>(),
				_mailRepository.Object,
				new MockEmail<IEmailSenderRecipient>(),
				_gMMResources,
				_jobTriggerConfig,
				new TelemetryClient(TelemetryConfiguration.CreateDefault()));

			await _jobTriggerService.SendEmailAsync(job, EmailSubject, SyncStartedEmailBody, new string[] { });
			var expectedLogMessage = $"Notifications of type '{SyncStartedEmailBody}' have been globally disabled.";
			_mailRepository.Verify(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>(), ""), Times.Never());
			Assert.AreEqual(2, _loggingRepository.MessagesLoggedCount);
			Assert.AreEqual(expectedLogMessage, _loggingRepository.MessagesLogged[0].Message);
		}

		[TestMethod]
        public async Task VerifyJobsCountExceedMinimalNumberHigherThanThreshold()
        {
            var jobsProceedNow = 5;
            var jobsNotProceedNow = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(jobsProceedNow, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(jobsNotProceedNow, Organization, startDateBase: DateTime.UtcNow.AddDays(5)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

            var response = await _jobTriggerService.GetSyncJobsAsync();
            var jobTriggerThresholdExceeded = response.jobTriggerThresholdExceeded;
            Assert.AreEqual(true, jobTriggerThresholdExceeded);
        }

        [TestMethod]
        public async Task VerifyJobsCountExceedMinimalNumberLowerThanThreshold()
        {
            var jobsProceedNow = 5;
            var jobsNotProceedNow = 20;

			_syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(jobsProceedNow, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(jobsNotProceedNow, Organization, startDateBase: DateTime.UtcNow.AddDays(5)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

            var response = await _jobTriggerService.GetSyncJobsAsync();
            var jobTriggerThresholdExceeded = response.jobTriggerThresholdExceeded;
            Assert.AreEqual(false, jobTriggerThresholdExceeded);
        }

		[TestMethod]
		public async Task VerifyJobsCountLowerThanMinimalHigherThanThreshold()
		{
			var jobsProceedNow = 3;
			var jobsNotProceedNow = 3;

			_syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(jobsProceedNow, Organization));
			_syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(jobsNotProceedNow, Organization, startDateBase: DateTime.UtcNow.AddDays(5)));

			_syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));
			_syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

			var response = await _jobTriggerService.GetSyncJobsAsync();
			var jobTriggerThresholdExceeded = response.jobTriggerThresholdExceeded;
			Assert.AreEqual(false, jobTriggerThresholdExceeded);
		}

		private class MockEmail<T> : IEmailSenderRecipient
        {
            public string SenderAddress => "";

            public string SenderPassword => "";

            public string SyncCompletedCCAddresses => "";

            public string SyncDisabledCCAddresses => "";

            public string SupportEmailAddresses => "";
        }

        private class MockKeyVaultSecret<T> : IKeyVaultSecret<T>
        {
            public string Secret => "";
        }
    }
}
