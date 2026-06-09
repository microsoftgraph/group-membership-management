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
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using Models.Entities;
using Newtonsoft.Json;
using Models.Notifications;
using Models.ServiceBus;
using Models.SyncJobHistory;
using Models.Helpers;
using Repositories.Contracts.Helpers;
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
            _serviceBusTopicsRepository = new MockServiceBusTopicsRepository();
            _graphGroupRepository = new MockGraphGroupRepository();
            _mockTeamsChannelRepository = new Mock<ITeamsChannelRepository>();
            _jobTriggerConfig = new MockJobTriggerConfig();
            _syncJobStatusService = new Mock<ISyncJobStatusService>();
            _syncJobStatusService.Setup(x => x.UpdateJobStatusAsync(It.IsAny<SyncJob>(), It.IsAny<SyncStatus?>(), It.IsAny<SyncJobHistory?>(), It.IsAny<string>()))
                .Callback<SyncJob, SyncStatus?, SyncJobHistory?, string>((job, status, history, functionName) => job.Status = status.ToString())
                .Returns(Task.CompletedTask);

            _jobTriggerService = new JobTriggerService(
                                        NullLogger<JobTriggerService>.Instance,
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
        public async Task VerifyTerminatingStatusSetsHistoryEndTime()
        {
            SyncJobHistory capturedHistory = null;

            _syncJobStatusService
                .Setup(x => x.UpdateJobStatusAsync(It.IsAny<SyncJob>(), SyncStatus.NotOwnerOfDestinationGroup, It.IsAny<SyncJobHistory?>(), It.IsAny<string>()))
                .Callback<SyncJob, SyncStatus?, SyncJobHistory?, string>((job, status, history, functionName) =>
                {
                    capturedHistory = history;
                    job.Status = status.ToString();
                })
                .Returns(Task.CompletedTask);

            var job = SampleDataHelper.CreateSampleSyncJobs(1, GroupMembership).First();
            job.RunId = Guid.NewGuid();

            await _jobTriggerService.UpdateSyncJobAsync(SyncStatus.NotOwnerOfDestinationGroup, job);

            Assert.IsNotNull(capturedHistory);
            Assert.AreEqual(job.Id, capturedHistory.SyncJobId);
            Assert.AreEqual(job.RunId.Value, capturedHistory.RunId);
            Assert.IsTrue(capturedHistory.EndTime.HasValue);
            Assert.IsFalse(capturedHistory.StartTime.HasValue);
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
                NullLogger<JobTriggerService>.Instance,
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
                using var activity = CorrelationActivity.StartSyncJobActivity(nameof(VerifyInitialSyncEmailNotificationIsSent), job);
                _ = await _graphGroupRepository.GetGroupNameAsync(job.Group.GroupId);
                await _jobTriggerService.SendEmailAsync(job, NotificationMessageType.SyncStartedNotification, new string[] { });

                Assert.IsNotNull(_graphGroupRepository.LastResolvedRunId);
                Assert.AreEqual(job.RunId.Value, _graphGroupRepository.LastResolvedRunId.Value);
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
                NullLogger<JobTriggerService>.Instance,
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
                NullLogger<JobTriggerService>.Instance,
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
                NullLogger<JobTriggerService>.Instance,
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
        public async Task GetSyncJobByIdAsync_ReturnsSyncJob_WhenJobExists()
        {
            // Arrange
            var testJob = SampleDataHelper.CreateSampleSyncJobs(1, Organization).First();
            _syncJobRepository.Jobs.Add(testJob);
            _graphGroupRepository.GroupsThatExist.Add(testJob.Group.GroupId);
            _graphGroupRepository.GroupsGMMOwns.Add(testJob.Group.GroupId);

            // Act
            var result = await _jobTriggerService.GetSyncJobByIdAsync(testJob.Id);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(testJob.Id, result.Id);
            Assert.AreEqual(testJob.TargetOfficeGroupId, result.TargetOfficeGroupId);
            Assert.AreEqual(testJob.Query, result.Query);
        }

        [TestMethod]
        public async Task GetSyncJobByIdAsync_ReturnsNull_WhenJobDoesNotExist()
        {
            // Arrange
            var nonExistentJobId = Guid.NewGuid();

            // Act
            var result = await _jobTriggerService.GetSyncJobByIdAsync(nonExistentJobId);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task GetSyncJobByIdAsync_ReturnsJobWithCurrentStatus_WhenStatusWasUpdated()
        {
            // Arrange
            var testJob = SampleDataHelper.CreateSampleSyncJobs(1, Organization).First();
            testJob.Status = SyncStatus.Idle.ToString();
            _syncJobRepository.Jobs.Add(testJob);
            _graphGroupRepository.GroupsThatExist.Add(testJob.Group.GroupId);
            _graphGroupRepository.GroupsGMMOwns.Add(testJob.Group.GroupId);

            // Update the job status
            await _jobTriggerService.UpdateSyncJobAsync(SyncStatus.InProgress, testJob);

            // Act
            var result = await _jobTriggerService.GetSyncJobByIdAsync(testJob.Id);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(SyncStatus.InProgress.ToString(), result.Status);
        }

        #region TryClaimAndUpdateJobAsync Tests

        [TestMethod]
        public async Task ClaimJob_IdleJob_ClaimsSuccessfully()
        {
            var job = SampleDataHelper.CreateSampleSyncJobs(1, Organization).First();
            job.Status = SyncStatus.Idle.ToString();
            job.RunId = Guid.NewGuid();
            job.Period = 24;
            _syncJobRepository.Jobs.Add(job);

            var result = await _jobTriggerService.TryClaimAndUpdateJobAsync(SyncStatus.InProgress, job);

            Assert.IsNotNull(result);
            var updatedJob = _syncJobRepository.Jobs.First(j => j.Id == job.Id);
            Assert.AreEqual(SyncStatus.InProgress.ToString(), updatedJob.Status);
            Assert.AreEqual(job.RunId, updatedJob.RunId);
        }

        [TestMethod]
        public async Task ClaimJob_TransientErrorJob_ClaimsSuccessfully()
        {
            var job = SampleDataHelper.CreateSampleSyncJobs(1, Organization).First();
            job.Status = SyncStatus.TransientError.ToString();
            job.RunId = Guid.NewGuid();
            job.Period = 24;
            _syncJobRepository.Jobs.Add(job);

            var result = await _jobTriggerService.TryClaimAndUpdateJobAsync(SyncStatus.InProgress, job);

            Assert.IsNotNull(result);
            Assert.AreEqual(SyncStatus.InProgress.ToString(), _syncJobRepository.Jobs.First().Status);
        }

        [TestMethod]
        public async Task ClaimJob_StuckInProgressJob_ClaimsSuccessfully()
        {
            var job = SampleDataHelper.CreateSampleSyncJobs(1, Organization).First();
            job.Status = SyncStatus.InProgress.ToString();
            job.RunId = Guid.NewGuid();
            job.Period = 24;
            job.LastSuccessfulStartTime = DateTime.UtcNow.AddHours(-25);
            _syncJobRepository.Jobs.Add(job);

            var result = await _jobTriggerService.TryClaimAndUpdateJobAsync(SyncStatus.StuckInProgress, job);

            Assert.IsNotNull(result);
            var updatedJob = _syncJobRepository.Jobs.First();
            Assert.AreEqual(SyncStatus.StuckInProgress.ToString(), updatedJob.Status);
        }

        [TestMethod]
        public async Task ClaimJob_StuckInProgressJob_SetsLastRunTime()
        {
            var job = SampleDataHelper.CreateSampleSyncJobs(1, Organization).First();
            job.Status = SyncStatus.InProgress.ToString();
            job.RunId = Guid.NewGuid();
            job.Period = 24;
            job.LastSuccessfulStartTime = DateTime.UtcNow.AddHours(-25);
            job.LastRunTime = DateTime.UtcNow.AddDays(-5);
            _syncJobRepository.Jobs.Add(job);

            var beforeClaim = DateTime.UtcNow;
            var result = await _jobTriggerService.TryClaimAndUpdateJobAsync(SyncStatus.StuckInProgress, job);

            Assert.IsNotNull(result);
            var updatedJob = _syncJobRepository.Jobs.First();
            Assert.IsTrue(updatedJob.LastRunTime >= beforeClaim);
        }

        [TestMethod]
        public async Task ClaimJob_IdleJob_DoesNotSetLastRunTime()
        {
            var job = SampleDataHelper.CreateSampleSyncJobs(1, Organization).First();
            job.Status = SyncStatus.Idle.ToString();
            job.RunId = Guid.NewGuid();
            job.Period = 24;
            var originalLastRunTime = DateTime.UtcNow.AddDays(-5);
            job.LastRunTime = originalLastRunTime;
            _syncJobRepository.Jobs.Add(job);

            await _jobTriggerService.TryClaimAndUpdateJobAsync(SyncStatus.InProgress, job);

            Assert.AreEqual(originalLastRunTime, _syncJobRepository.Jobs.First().LastRunTime);
        }

        [TestMethod]
        public async Task ClaimJob_FreshInProgressJob_RejectsClaim()
        {
            var job = SampleDataHelper.CreateSampleSyncJobs(1, Organization).First();
            job.Status = SyncStatus.InProgress.ToString();
            job.RunId = Guid.NewGuid();
            job.Period = 24;
            job.LastSuccessfulStartTime = DateTime.UtcNow.AddHours(-1);
            _syncJobRepository.Jobs.Add(job);

            var result = await _jobTriggerService.TryClaimAndUpdateJobAsync(SyncStatus.InProgress, job);

            Assert.IsNull(result);
            Assert.AreEqual(SyncStatus.InProgress.ToString(), _syncJobRepository.Jobs.First().Status);
        }

        [TestMethod]
        public async Task ClaimJob_StuckInProgressStatus_RejectsClaim()
        {
            var job = SampleDataHelper.CreateSampleSyncJobs(1, Organization).First();
            job.Status = SyncStatus.StuckInProgress.ToString();
            job.RunId = Guid.NewGuid();
            job.Period = 24;
            _syncJobRepository.Jobs.Add(job);

            var result = await _jobTriggerService.TryClaimAndUpdateJobAsync(SyncStatus.InProgress, job);

            Assert.IsNull(result);
            Assert.AreEqual(SyncStatus.StuckInProgress.ToString(), _syncJobRepository.Jobs.First().Status);
        }

        [TestMethod]
        public async Task ClaimJob_ErrorStatus_RejectsClaim()
        {
            var job = SampleDataHelper.CreateSampleSyncJobs(1, Organization).First();
            job.Status = SyncStatus.Error.ToString();
            job.RunId = Guid.NewGuid();
            job.Period = 24;
            _syncJobRepository.Jobs.Add(job);

            var result = await _jobTriggerService.TryClaimAndUpdateJobAsync(SyncStatus.InProgress, job);

            Assert.IsNull(result);
            Assert.AreEqual(SyncStatus.Error.ToString(), _syncJobRepository.Jobs.First().Status);
        }

        [TestMethod]
        public async Task ClaimJob_ConcurrentClaims_OnlyOneSucceeds()
        {
            var job = SampleDataHelper.CreateSampleSyncJobs(1, Organization).First();
            job.Status = SyncStatus.Idle.ToString();
            job.RunId = Guid.NewGuid();
            job.Period = 24;
            _syncJobRepository.Jobs.Add(job);

            var firstClaim = await _jobTriggerService.TryClaimAndUpdateJobAsync(SyncStatus.InProgress, job);
            var secondClaim = await _jobTriggerService.TryClaimAndUpdateJobAsync(SyncStatus.InProgress, job);

            Assert.IsNotNull(firstClaim);
            Assert.IsNull(secondClaim);
        }

        [TestMethod]
        public async Task ClaimJob_ConcurrentClaims_StuckJob_OnlyOneSucceeds()
        {
            var job = SampleDataHelper.CreateSampleSyncJobs(1, Organization).First();
            job.Status = SyncStatus.InProgress.ToString();
            job.RunId = Guid.NewGuid();
            job.Period = 24;
            job.LastSuccessfulStartTime = DateTime.UtcNow.AddHours(-25);
            _syncJobRepository.Jobs.Add(job);

            var firstClaim = await _jobTriggerService.TryClaimAndUpdateJobAsync(SyncStatus.StuckInProgress, job);
            var secondClaim = await _jobTriggerService.TryClaimAndUpdateJobAsync(SyncStatus.StuckInProgress, job);

            Assert.IsNotNull(firstClaim);
            Assert.IsNull(secondClaim);
        }

        [TestMethod]
        public async Task ClaimJob_NonexistentJob_ReturnsNull()
        {
            var job = SampleDataHelper.CreateSampleSyncJobs(1, Organization).First();
            job.Status = SyncStatus.Idle.ToString();
            job.RunId = Guid.NewGuid();
            job.Period = 24;
            // NOT added to repository

            var result = await _jobTriggerService.TryClaimAndUpdateJobAsync(SyncStatus.InProgress, job);

            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task ClaimJob_InProgressAtBoundary_RejectsClaim()
        {
            var job = SampleDataHelper.CreateSampleSyncJobs(1, Organization).First();
            job.Status = SyncStatus.InProgress.ToString();
            job.RunId = Guid.NewGuid();
            job.Period = 24;
            job.LastSuccessfulStartTime = DateTime.UtcNow.AddHours(-24).AddMinutes(1);
            _syncJobRepository.Jobs.Add(job);

            var result = await _jobTriggerService.TryClaimAndUpdateJobAsync(SyncStatus.StuckInProgress, job);

            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task ClaimJob_SetsLastSuccessfulStartTime()
        {
            var job = SampleDataHelper.CreateSampleSyncJobs(1, Organization).First();
            job.Status = SyncStatus.Idle.ToString();
            job.RunId = Guid.NewGuid();
            job.Period = 24;
            job.LastSuccessfulStartTime = DateTime.UtcNow.AddDays(-10);
            _syncJobRepository.Jobs.Add(job);

            var beforeClaim = DateTime.UtcNow;
            await _jobTriggerService.TryClaimAndUpdateJobAsync(SyncStatus.InProgress, job);

            var updatedJob = _syncJobRepository.Jobs.First();
            Assert.IsTrue(updatedJob.LastSuccessfulStartTime >= beforeClaim);
        }

        #endregion

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
