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
using Models.Entities;
using Newtonsoft.Json;
using Models.Notifications;
using Models.ServiceBus;
using Models.SyncJobHistory;
using Models.Helpers;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Services.Tests
{
    [TestClass]
    public class JobTriggerServiceTests
    {
        private JobTriggerService _jobTriggerService = null;
        private MockDatabaseSyncJobRepository _syncJobRepository = null;
        private MockDatabaseGroupsRepository _groupsRepository = null;
        private MockDatabaseChannelsRepository _channelsRepository = null;
        private Mock<IDatabaseDestinationAttributesRepository> _destinationAttributesRepository = null;
        private MockNotificationTypesRepository _notificationTypesRepository = null;
        private MockJobNotificationRepository _jobNotificationRepository = null;
        private MockLoggingRepository _loggingRepository = null;
        private MockServiceBusTopicsRepository _serviceBusTopicsRepository = null;
        private MockGraphGroupRepository _graphGroupRepository;
        private Mock<ITeamsChannelRepository> _mockTeamsChannelRepository = null;
        private MockMailRepository _mailRepository = null;
        private GMMResources _gMMResources = null;
        private MockJobTriggerConfig _jobTriggerConfig = null;
        private Mock<IServiceBusQueueRepository> _serviceBusQueueRepository;
        private Mock<ISyncJobStatusService> _syncJobStatusService = null;

        private const string Organization = "Organization";
        private const string GroupMembership = "GroupMembership";
        private const string SyncDisabledNoGroupEmailBody = "SyncDisabledNoGroupEmailBody";

        private JsonSerializerOptions _destinationObjectSerializerOptions;

        [TestInitialize]
        public void InitializeTest()
        {
            _gMMResources = new GMMResources
            {
                LearnMoreAboutGMMUrl = "http://learn-more-about-gmm"
            };
            _serviceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            _syncJobRepository = new MockDatabaseSyncJobRepository();
            _groupsRepository = new MockDatabaseGroupsRepository();
            _channelsRepository = new MockDatabaseChannelsRepository();
            _destinationAttributesRepository = new Mock<IDatabaseDestinationAttributesRepository>();
            _notificationTypesRepository = new MockNotificationTypesRepository();
            _jobNotificationRepository = new MockJobNotificationRepository();
            _loggingRepository = new MockLoggingRepository();
            _serviceBusTopicsRepository = new MockServiceBusTopicsRepository();
            _graphGroupRepository = new MockGraphGroupRepository();
            _mockTeamsChannelRepository = new Mock<ITeamsChannelRepository>();
            _jobTriggerConfig = new MockJobTriggerConfig();
            _syncJobStatusService = new Mock<ISyncJobStatusService>();
            _syncJobStatusService.Setup(x => x.UpdateJobStatusAsync(It.IsAny<SyncJob>(), It.IsAny<SyncStatus?>(), It.IsAny<SyncJobHistory?>(), It.IsAny<string>()))
                .Callback<SyncJob, SyncStatus?, SyncJobHistory?, string>((job, status, history, functionName) => job.Status = status.ToString())
                .Returns(Task.CompletedTask);

            _jobTriggerService = new JobTriggerService(
                                        _loggingRepository,
                                        _syncJobRepository,
                                        _groupsRepository,
                                        _channelsRepository,
                                        _destinationAttributesRepository.Object,
                                        _notificationTypesRepository,
                                        _jobNotificationRepository,
                                        _serviceBusTopicsRepository,
                                        _graphGroupRepository,
                                        _mockTeamsChannelRepository.Object,
                                        new MockKeyVaultSecret<IJobTriggerService>(),
                                        new MockKeyVaultSecret<IJobTriggerService, Guid>(),
                                        new MockEmail<IEmailSenderRecipient>(),
                                        _serviceBusQueueRepository.Object,
                                        _gMMResources,
                                        _jobTriggerConfig,
                                        new TelemetryClient(TelemetryConfiguration.CreateDefault()),
                                        _syncJobStatusService.Object);

            _destinationObjectSerializerOptions = new JsonSerializerOptions { Converters = { new DestinationValueConverter() } };
        }

        [TestMethod]
        public async Task TestValidGroupDestinationQuery()
        {
            SyncJob job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            var objectId = Guid.NewGuid();
            job.MembershipType = "GroupMembership";
            job.Group = new Group
            {
                GroupId = objectId
            };

            var parsedAndValidated = await _jobTriggerService.ParseAndValidateDestinationAsync(job);

            Assert.AreEqual(true, parsedAndValidated.IsValid);

            var destinationObject = JsonSerializer.Deserialize<DestinationObject>(parsedAndValidated.DestinationObject, _destinationObjectSerializerOptions);

            Assert.AreEqual(objectId, destinationObject.Value.ObjectId);
        }

        [TestMethod]
        public async Task TestValidTeamsDestinationQuery()
        {
            SyncJob job = SampleDataHelper.CreateSampleSyncJobsWithTeamsChannelMembership(1, GroupMembership).First();
            var objectId = Guid.NewGuid();
            var channelId = "Channel_ID";
            job.MembershipType = "TeamsChannelMembership";
            job.Channel = new Channel
            {
                GroupId = objectId,
                ChannelId = channelId
            };

            var parsedAndValidated = await _jobTriggerService.ParseAndValidateDestinationAsync(job);

            Assert.AreEqual(true, parsedAndValidated.IsValid);

            var destinationObject = JsonSerializer.Deserialize<DestinationObject>(parsedAndValidated.DestinationObject, _destinationObjectSerializerOptions);

            Assert.AreEqual(objectId, destinationObject.Value.ObjectId);
            Assert.AreEqual(channelId, (destinationObject.Value as TeamsChannelDestinationValue).ChannelId);
        }

        [TestMethod]
        public async Task TestEmptyDestinationQuery()
        {
            SyncJob job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            job.Group = null;

            var parsedAndValidated = await _jobTriggerService.ParseAndValidateDestinationAsync(job);

            Assert.AreEqual(false, parsedAndValidated.IsValid);
            Assert.AreEqual(null, parsedAndValidated.DestinationObject);
        }

        [TestMethod]
        public async Task TestInvalidDestinationQueryDueToMissingType()
        {
            SyncJob job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            job.MembershipType = null;

            var parsedAndValidated = await _jobTriggerService.ParseAndValidateDestinationAsync(job);

            Assert.AreEqual(false, parsedAndValidated.IsValid);
            Assert.AreEqual(null, parsedAndValidated.DestinationObject);
        }

        [TestMethod]
        public async Task TestInvalidTeamsDestinationQuery()
        {
            SyncJob job = SampleDataHelper.CreateSampleSyncJobsWithTeamsChannelMembership(1, GroupMembership).First();
            job.Channel = null;

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

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.Group.GroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.Group.GroupId));

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

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.Group.GroupId));

            foreach (var job in _syncJobRepository.Jobs.Take(enabledJobs))
            {
                var response = await _jobTriggerService.DestinationExistsAndGMMCanWriteToItAsync(job);
                Assert.AreEqual(DestinationVerifierResult.NotFound, response);
            }
        }

        [TestMethod]
        public async Task VerifyJobsWithNonexistentTargetTeamAreErrored()
        {
            var enabledJobs = 5;
            var disabledJobs = 3;

            _mockTeamsChannelRepository.Setup<Task<bool>>(repo => repo.IsServiceAccountOwnerOfChannelAsync(It.IsAny<Guid>(), It.IsAny<AzureADTeamsChannel>(), It.IsAny<Guid>()))
                .ReturnsAsync(false);

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobsWithTeamsChannelMembership(enabledJobs, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobsWithTeamsChannelMembership(disabledJobs, Organization));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.Channel.GroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.Channel.GroupId));

            foreach (var job in _syncJobRepository.Jobs.Take(enabledJobs))
            {
                var response = await _jobTriggerService.DestinationExistsAndGMMCanWriteToItAsync(job);
                Assert.AreEqual(DestinationVerifierResult.NotFound, response);
            }
        }

        [TestMethod]
        public async Task VerifyJobsWithNonexistentTargetChannelsAreErrored()
        {
            var enabledJobs = 5;
            var disabledJobs = 3;

            _mockTeamsChannelRepository.Setup<Task<bool>>(repo => repo.TeamsChannelExistsAsync(It.IsAny<AzureADTeamsChannel>(), It.IsAny<Guid>()))
                .ReturnsAsync(false);

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobsWithTeamsChannelMembership(enabledJobs, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobsWithTeamsChannelMembership(disabledJobs, Organization));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.Channel.GroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.Channel.GroupId));

            foreach (var job in _syncJobRepository.Jobs.Take(enabledJobs))
            {
                var response = await _jobTriggerService.DestinationExistsAndGMMCanWriteToItAsync(job);
                Assert.AreEqual(DestinationVerifierResult.NotFound, response);
            }
        }

        [TestMethod]
        public async Task VerifyJobsWithGroupDestinationsGMMDoesntOwnAreErrored()
        {
            var enabledJobs = 5;
            var disabledJobs = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(enabledJobs, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(disabledJobs, Organization));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.Group.GroupId));

            foreach (var job in _syncJobRepository.Jobs.Take(enabledJobs))
            {
                var response = await _jobTriggerService.DestinationExistsAndGMMCanWriteToItAsync(job);
                Assert.AreEqual(DestinationVerifierResult.NotOwnedByGMM, response);
            }
        }

        [TestMethod]
        public async Task VerifyJobsWithChannelDestinationsGMMDoesntOwnAreErrored()
        {
            var enabledJobs = 5;
            var disabledJobs = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobsWithTeamsChannelMembership(enabledJobs, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobsWithTeamsChannelMembership(disabledJobs, Organization));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.Channel.GroupId));

            _mockTeamsChannelRepository.Setup<Task<bool>>(repo => repo.TeamsChannelExistsAsync(It.IsAny<AzureADTeamsChannel>(), It.IsAny<Guid>()))
                .ReturnsAsync(true);

            // Fails for jobs where teams channel exists but service account is not owner
            foreach (var job in _syncJobRepository.Jobs.Take(enabledJobs))
            {
                var response = await _jobTriggerService.DestinationExistsAndGMMCanWriteToItAsync(job);
                Assert.AreEqual(DestinationVerifierResult.NotOwnedByGMM, response);
            }

            _mockTeamsChannelRepository.Setup<Task<bool>>(repo => repo.IsServiceAccountOwnerOfChannelAsync(It.IsAny<Guid>(), It.IsAny<AzureADTeamsChannel>(), It.IsAny<Guid>()))
                .ReturnsAsync(true);

            // Passes now that service account is owner
            foreach (var job in _syncJobRepository.Jobs.Take(enabledJobs))
            {
                var response = await _jobTriggerService.DestinationExistsAndGMMCanWriteToItAsync(job);
                Assert.AreEqual(DestinationVerifierResult.Success, response);
            }
        }

        [TestMethod]
        public async Task VerifyJobsWithValidStartDateAreProcessed()
        {
            var validStartDateJobs = 5;
            var futureStartDateJobs = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(validStartDateJobs, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(futureStartDateJobs, Organization, startDateBase: DateTime.UtcNow.AddDays(5)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.Group.GroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.Group.GroupId));

            var jobs = await _jobTriggerService.GetSyncJobsAsync();

            var jobsToProcessCount = _serviceBusTopicsRepository.Subscriptions.Sum(x => x.Value.Count);

            Assert.AreEqual(validStartDateJobs, jobs.Count);
        }

        [TestMethod]
        public async Task VerifyJobsWithValidScheduledDateAreProcessed()
        {
            var validStartDateJobs = 5;
            var futureStartDateJobs = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(validStartDateJobs, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(futureStartDateJobs, Organization, scheduledDateBase: DateTime.UtcNow.AddDays(5)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.Group.GroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.Group.GroupId));

            var jobs = await _jobTriggerService.GetSyncJobsAsync();

            var jobsToProcessCount = _serviceBusTopicsRepository.Subscriptions.Sum(x => x.Value.Count);

            Assert.AreEqual(validStartDateJobs, jobs.Count);
        }

        [TestMethod]
        public async Task VerifyJobsWithValidPeriodsAreProcessed()
        {
            var jobsWithValidPeriods = 5;
            var jobsWithInvalidPeriods = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(jobsWithValidPeriods, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(jobsWithInvalidPeriods, Organization, scheduledDateBase: DateTime.UtcNow.AddDays(5)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.Group.GroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.Group.GroupId));

            var jobs = await _jobTriggerService.GetSyncJobsAsync();

            var jobsToProcessCount = _serviceBusTopicsRepository.Subscriptions.Sum(x => x.Value.Count);

            Assert.AreEqual(jobsWithValidPeriods, jobs.Count);
        }

        [TestMethod]
        public async Task VerifyJobStatusIsUpdatedToInProgress()
        {
            var jobs = 2;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(jobs, Organization));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.Group.GroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.Group.GroupId));

            foreach (var job in _syncJobRepository.Jobs)
            {
                var canWriteToGroup = await _jobTriggerService.DestinationExistsAndGMMCanWriteToItAsync(job);
                await _jobTriggerService.UpdateSyncJobAsync(canWriteToGroup == DestinationVerifierResult.Success ? SyncStatus.InProgress : SyncStatus.NotOwnerOfDestinationGroup, job);
                Assert.AreEqual(job.Status, SyncStatus.InProgress.ToString());
            }
        }

        [TestMethod]
        public async Task VerifyJobStatusIsUpdatedToStuckInProgress()
        {
            var jobs = 2;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(jobs, Organization));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.Group.GroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.Group.GroupId));

            foreach (var job in _syncJobRepository.Jobs)
            {
                job.Status = SyncStatus.InProgress.ToString();
                var canWriteToGroup = await _jobTriggerService.DestinationExistsAndGMMCanWriteToItAsync(job);
                await _jobTriggerService.UpdateSyncJobAsync(canWriteToGroup == DestinationVerifierResult.Success ? SyncStatus.StuckInProgress : SyncStatus.NotOwnerOfDestinationGroup, job);
                Assert.AreEqual(job.Status, SyncStatus.StuckInProgress.ToString());
            }
        }

        [TestMethod]
        public async Task VerifyJobStatusIsUpdatedToErrorDueToNoGroupOwnership()
        {
            var jobs = 2;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(jobs, Organization));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.Group.GroupId));

            foreach (var job in _syncJobRepository.Jobs)
            {
                var canWriteToGroup = await _jobTriggerService.DestinationExistsAndGMMCanWriteToItAsync(job);
                await _jobTriggerService.UpdateSyncJobAsync(canWriteToGroup == DestinationVerifierResult.Success ? SyncStatus.InProgress : SyncStatus.NotOwnerOfDestinationGroup, job);
                Assert.AreEqual(job.Status, SyncStatus.NotOwnerOfDestinationGroup.ToString());
            }
        }


        [TestMethod]
        public async Task VerifyJobStatusIsUpdatedToInProgressDueToTenantAPIPermissions()
        {
            var jobs = 2;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(jobs, Organization));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.Group.GroupId));

            _jobTriggerConfig.GMMHasGroupReadWriteAllPermissions = true;

            foreach (var job in _syncJobRepository.Jobs)
            {
                var canWriteToGroup = await _jobTriggerService.DestinationExistsAndGMMCanWriteToItAsync(job);
                await _jobTriggerService.UpdateSyncJobAsync(canWriteToGroup == DestinationVerifierResult.Success ? SyncStatus.InProgress : SyncStatus.NotOwnerOfDestinationGroup, job);
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

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.Group.GroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.Group.GroupId));

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

            _jobTriggerService = new JobTriggerService(
                _loggingRepository,
                _syncJobRepository,
                _groupsRepository,
                _channelsRepository,
                _destinationAttributesRepository.Object,
                _notificationTypesRepository,
                _jobNotificationRepository,
                _serviceBusTopicsRepository,
                _graphGroupRepository,
                _mockTeamsChannelRepository.Object,
                new MockKeyVaultSecret<IJobTriggerService>(),
                new MockKeyVaultSecret<IJobTriggerService, Guid>(),
                new MockEmail<IEmailSenderRecipient>(),
                _serviceBusQueueRepository.Object,
                _gMMResources,
                _jobTriggerConfig,
       new TelemetryClient(TelemetryConfiguration.CreateDefault()),
       _syncJobStatusService.Object);

            var validStartDateJobs = 5;
            var futureStartDateJobs = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(validStartDateJobs, Organization, lastRunTime: SqlDateTime.MinValue.Value));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(futureStartDateJobs, Organization, startDateBase: DateTime.UtcNow.AddDays(5), lastRunTime: SqlDateTime.MinValue.Value));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.Group.GroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.Group.GroupId));

            var jobs = await _jobTriggerService.GetSyncJobsAsync();

            foreach (var job in jobs)
            {
                _jobTriggerService.RunId = job.RunId.Value;
                var groupName = await _graphGroupRepository.GetGroupNameAsync(job.Group.GroupId);
                await _jobTriggerService.SendEmailAsync(job, NotificationMessageType.SyncStartedNotification, new string[] { });

                Assert.IsNotNull(_jobTriggerService.RunId);
                Assert.IsNotNull(_graphGroupRepository.RunId);
                Assert.AreEqual(_jobTriggerService.RunId, _graphGroupRepository.RunId);
            }

            Assert.AreEqual(validStartDateJobs, jobs.Count);
            _serviceBusQueueRepository.Verify(x => x.SendMessageAsync(It.Is<ServiceBusMessage>(msg =>
                    msg.ApplicationProperties.ContainsKey("MessageType") &&
                    msg.ApplicationProperties["MessageType"].ToString() == NotificationMessageType.SyncStartedNotification.ToString())),
                    Times.Exactly(validStartDateJobs));
        }

        [TestMethod]
        public async Task VerifyJobsAreProcessedWithMissingMailSendPermission()
        {
            _jobTriggerService = new JobTriggerService(
                _loggingRepository,
                _syncJobRepository,
                _groupsRepository,
                _channelsRepository,
                _destinationAttributesRepository.Object,
                _notificationTypesRepository,
       _jobNotificationRepository,
       _serviceBusTopicsRepository,
                _graphGroupRepository,
                _mockTeamsChannelRepository.Object,
                new MockKeyVaultSecret<IJobTriggerService>(),
                new MockKeyVaultSecret<IJobTriggerService, Guid>(),
                new MockEmail<IEmailSenderRecipient>(),
                _serviceBusQueueRepository.Object,
                _gMMResources,
                _jobTriggerConfig,
       new TelemetryClient(TelemetryConfiguration.CreateDefault()),
       _syncJobStatusService.Object);

            var validStartDateJobs = 5;
            var futureStartDateJobs = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(validStartDateJobs, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(futureStartDateJobs, Organization, startDateBase: DateTime.UtcNow.AddDays(5)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.Group.GroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.Group.GroupId));

            var jobs = await _jobTriggerService.GetSyncJobsAsync();

            foreach (var job in jobs)
            {
                var groupName = await _graphGroupRepository.GetGroupNameAsync(job.Group.GroupId);
                await _jobTriggerService.SendEmailAsync(job,NotificationMessageType.SyncStartedNotification, new string[] { });
            }

            Assert.AreEqual(validStartDateJobs, jobs.Count);
        }

        [TestMethod]
        public async Task VerifyJobsAreProcessedWithMissingMailLicenses()
        {

            _jobTriggerService = new JobTriggerService(
                _loggingRepository,
                _syncJobRepository,
                _groupsRepository,
                _channelsRepository,
                _destinationAttributesRepository.Object,
                _notificationTypesRepository,
       _jobNotificationRepository,
       _serviceBusTopicsRepository,
                _graphGroupRepository,
                _mockTeamsChannelRepository.Object,
                new MockKeyVaultSecret<IJobTriggerService>(),
                new MockKeyVaultSecret<IJobTriggerService, Guid>(),
                new MockEmail<IEmailSenderRecipient>(),
                _serviceBusQueueRepository.Object,
                _gMMResources,
                _jobTriggerConfig,
       new TelemetryClient(TelemetryConfiguration.CreateDefault()),
       _syncJobStatusService.Object);

            var validStartDateJobs = 5;
            var futureStartDateJobs = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(validStartDateJobs, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(futureStartDateJobs, Organization, startDateBase: DateTime.UtcNow.AddDays(5)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.Group.GroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.Group.GroupId));

            var jobs = await _jobTriggerService.GetSyncJobsAsync();

            foreach (var job in jobs)
            {
                await _jobTriggerService.SendEmailAsync(job, NotificationMessageType.SyncStartedNotification, new string[] { });
            }

            Assert.AreEqual(validStartDateJobs, jobs.Count);
        }

        [TestMethod]
        public async Task VerifyJobsAreProcessedMailingExceptions()
        {

            _jobTriggerService = new JobTriggerService(
                _loggingRepository,
                _syncJobRepository,
                _groupsRepository,
                _channelsRepository,
                _destinationAttributesRepository.Object,
                _notificationTypesRepository,
       _jobNotificationRepository,
       _serviceBusTopicsRepository,
                _graphGroupRepository,
                _mockTeamsChannelRepository.Object,
                new MockKeyVaultSecret<IJobTriggerService>(),
                new MockKeyVaultSecret<IJobTriggerService, Guid>(),
                new MockEmail<IEmailSenderRecipient>(),
                _serviceBusQueueRepository.Object,
                _gMMResources,
                _jobTriggerConfig,
       new TelemetryClient(TelemetryConfiguration.CreateDefault()),
       _syncJobStatusService.Object);

            var validStartDateJobs = 5;
            var futureStartDateJobs = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(validStartDateJobs, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(futureStartDateJobs, Organization, startDateBase: DateTime.UtcNow.AddDays(5)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.Group.GroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.Group.GroupId));

            var jobs = await _jobTriggerService.GetSyncJobsAsync();

            foreach (var job in jobs)
            {
                await _jobTriggerService.SendEmailAsync(job, NotificationMessageType.SyncStartedNotification, new string[] { });
            }

            Assert.AreEqual(validStartDateJobs, jobs.Count);
        }

        private class MockEmail<T> : IEmailSenderRecipient
        {
            public string SenderAddress => "";

            public string SenderPassword => "";

            public string SupportEmailAddresses => "";
        }

        private class MockKeyVaultSecret<T> : IKeyVaultSecret<T>
        {
            public string Secret => "";
        }

        private class MockKeyVaultSecret<TType, TSecret> : IKeyVaultSecret<TType, TSecret>
        {
            public TSecret Secret => default;
        }
    }
}
