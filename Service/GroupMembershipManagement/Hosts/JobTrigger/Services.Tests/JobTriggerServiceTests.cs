// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using DIConcreteTypes;
using Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.Mocks;
using Repositories.ServiceBusTopics.Tests;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tests.Repositories;
using MockSyncJobRepository = Repositories.SyncJobs.Tests.MockSyncJobRepository;

namespace Services.Tests
{
    [TestClass]
    public class JobTriggerServiceTests
    {
        private JobTriggerService _jobTriggerService = null;
        private MockSyncJobRepository _syncJobRepository = null;
        private MockLoggingRepository _loggingRepository = null;
        private MockServiceBusTopicsRepository _serviceBusTopicsRepository = null;
        private MockGraphGroupRepository _graphGroupRepository;
        private MockMailRepository _mailRepository = null;
        private GMMResources _gMMResources = null;
        private MockJobTriggerConfig _jobTriggerConfig = null;

        private const string Organization = "Organization";
        private const string SecurityGroup = "SecurityGroup";
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

            _syncJobRepository = new MockSyncJobRepository();
            _loggingRepository = new MockLoggingRepository();
            _serviceBusTopicsRepository = new MockServiceBusTopicsRepository();
            _graphGroupRepository = new MockGraphGroupRepository();
            _mailRepository = new MockMailRepository();
            _jobTriggerConfig = new MockJobTriggerConfig();
            _jobTriggerService = new JobTriggerService(
                                        _loggingRepository,
                                        _syncJobRepository,
                                        _serviceBusTopicsRepository,
                                        _graphGroupRepository,
                                        new MockKeyVaultSecret<IJobTriggerService>(), _mailRepository,
                                        new MockEmail<IEmailSenderRecipient>(),
                                        _gMMResources,
                                        _jobTriggerConfig);
        }

        [TestMethod]
        public async Task ValidateJobsAreAddedToCorrectSubscription()
        {
            var organizationJobCount = 5;
            var securityGroupJobCount = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(organizationJobCount, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(securityGroupJobCount, SecurityGroup));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

            foreach (var job in _syncJobRepository.Jobs)
            {
                await _jobTriggerService.SendMessageAsync(job);
            }
            Assert.AreEqual(organizationJobCount, _serviceBusTopicsRepository.Subscriptions[Organization].Count);
            Assert.AreEqual(securityGroupJobCount, _serviceBusTopicsRepository.Subscriptions[SecurityGroup].Count);
        }

        [TestMethod]
        public async Task VerifyJobsWithNonexistentTargetGroupsAreErrored()
        {
            var enabledJobs = 5;
            var disabledJobs = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(enabledJobs, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(disabledJobs, Organization));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

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

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));

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

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

            var jobs = await _jobTriggerService.GetSyncJobsAsync(SyncStatus.Idle);

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

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

            var jobs = await _jobTriggerService.GetSyncJobsAsync(SyncStatus.Idle);

            var jobsToProcessCount = _serviceBusTopicsRepository.Subscriptions.Sum(x => x.Value.Count);

            Assert.AreEqual(jobsWithValidPeriods, jobs.Count);
        }

        [TestMethod]
        public async Task VerifyJobStatusIsUpdatedToInProgress()
        {
            var jobs = 2;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(jobs, Organization));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

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

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

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
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));

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
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));

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

            var securityGroupJobCount = 1;
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(securityGroupJobCount, SecurityGroup));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

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
            _mailRepository.Setup(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>()));

            _jobTriggerService = new JobTriggerService(
                _loggingRepository,
                _syncJobRepository,
                _serviceBusTopicsRepository,
                _graphGroupRepository,
                new MockKeyVaultSecret<IJobTriggerService>(),
                _mailRepository.Object,
                new MockEmail<IEmailSenderRecipient>(),
                _gMMResources,
                _jobTriggerConfig);

            var validStartDateJobs = 5;
            var futureStartDateJobs = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(validStartDateJobs, Organization, lastRunTime: DateTime.FromFileTimeUtc(0)));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(futureStartDateJobs, Organization, startDateBase: DateTime.UtcNow.AddDays(5), lastRunTime: DateTime.FromFileTimeUtc(0)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

            var jobs = await _jobTriggerService.GetSyncJobsAsync(SyncStatus.Idle);
            foreach (var job in jobs)
            {
                _jobTriggerService.RunId = job.RunId.Value;
                var groupName = await _graphGroupRepository.GetGroupNameAsync(job.TargetOfficeGroupId);
                await _jobTriggerService.SendEmailAsync(job, EmailSubject, SyncStartedEmailBody, new string[] { });

                Assert.IsNotNull(_jobTriggerService.RunId);
                Assert.IsNotNull(_graphGroupRepository.RunId);
                Assert.AreEqual(_jobTriggerService.RunId, _graphGroupRepository.RunId);
            }

            Assert.AreEqual(validStartDateJobs, jobs.Count);
            _mailRepository.Verify(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>()), Times.Exactly(validStartDateJobs));
        }

        [TestMethod]
        public async Task VerifyJobsAreProcessedWithMissingMailSendPermission()
        {
            var _mailRepository = new Mock<IMailRepository>();
            _mailRepository.Setup(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>()));

            _jobTriggerService = new JobTriggerService(
                _loggingRepository,
                _syncJobRepository,
                _serviceBusTopicsRepository,
                _graphGroupRepository,
                new MockKeyVaultSecret<IJobTriggerService>(),
                _mailRepository.Object,
                new MockEmail<IEmailSenderRecipient>(),
                _gMMResources,
                _jobTriggerConfig);

            var validStartDateJobs = 5;
            var futureStartDateJobs = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(validStartDateJobs, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(futureStartDateJobs, Organization, startDateBase: DateTime.UtcNow.AddDays(5)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

            var jobs = await _jobTriggerService.GetSyncJobsAsync(SyncStatus.Idle);
            foreach (var job in jobs)
            {
                var groupName = await _graphGroupRepository.GetGroupNameAsync(job.TargetOfficeGroupId);
                await _jobTriggerService.SendEmailAsync(job, EmailSubject, SyncStartedEmailBody, new string[] { });
            }

            Assert.AreEqual(validStartDateJobs, jobs.Count);
        }

        [TestMethod]
        public async Task VerifyJobsAreProcessedWithMissingMailLicenses()
        {
            var _mailRepository = new Mock<IMailRepository>();
            _mailRepository.Setup(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>()));

            _jobTriggerService = new JobTriggerService(
                _loggingRepository,
                _syncJobRepository,
                _serviceBusTopicsRepository,
                _graphGroupRepository,
                new MockKeyVaultSecret<IJobTriggerService>(),
                _mailRepository.Object,
                new MockEmail<IEmailSenderRecipient>(),
                _gMMResources,
                _jobTriggerConfig);

            var validStartDateJobs = 5;
            var futureStartDateJobs = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(validStartDateJobs, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(futureStartDateJobs, Organization, startDateBase: DateTime.UtcNow.AddDays(5)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

            var jobs = await _jobTriggerService.GetSyncJobsAsync(SyncStatus.Idle);
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
            _mailRepository.Setup(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid?>()));

            _jobTriggerService = new JobTriggerService(
                _loggingRepository,
                _syncJobRepository,
                _serviceBusTopicsRepository,
                _graphGroupRepository,
                new MockKeyVaultSecret<IJobTriggerService>(),
                _mailRepository.Object,
                new MockEmail<IEmailSenderRecipient>(),
                _gMMResources,
                _jobTriggerConfig);

            var validStartDateJobs = 5;
            var futureStartDateJobs = 3;

            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(validStartDateJobs, Organization));
            _syncJobRepository.Jobs.AddRange(SampleDataHelper.CreateSampleSyncJobs(futureStartDateJobs, Organization, startDateBase: DateTime.UtcNow.AddDays(5)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

            var jobs = await _jobTriggerService.GetSyncJobsAsync(SyncStatus.Idle);
            foreach (var job in jobs)
            {
                await _jobTriggerService.SendEmailAsync(job, EmailSubject, SyncStartedEmailBody, new string[] { });
            }

            Assert.AreEqual(validStartDateJobs, jobs.Count);
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
