// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Repositories.SyncJobs.Tests;
using Repositories.Mocks;
using Tests.Repositories;
using Repositories.ServiceBusTopics.Tests;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Moq;
using Repositories.Contracts;
using Microsoft.Identity.Client;
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

        private const string Organization = "Organization";
        private const string SecurityGroup = "SecurityGroup";

        [TestInitialize]
        public void InitializeTest()
        {
            _syncJobRepository = new MockSyncJobRepository();
            _loggingRepository = new MockLoggingRepository();
            _serviceBusTopicsRepository = new MockServiceBusTopicsRepository();
            _graphGroupRepository = new MockGraphGroupRepository();
            _mailRepository = new MockMailRepository();
            _jobTriggerService = new JobTriggerService(_loggingRepository, _syncJobRepository, _serviceBusTopicsRepository, _graphGroupRepository, new MockKeyVaultSecret<IJobTriggerService>(), _mailRepository, new MockEmail<IEmailSenderRecipient>());
        }

        [TestMethod]
        public async Task ValidateJobsAreAddedToCorrectSubscription()
        {
            var organizationJobCount = 5;
            var securityGroupJobCount = 3;

            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(organizationJobCount, Organization));
            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(securityGroupJobCount, SecurityGroup));

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
        public async Task VerifyEnabledJobsAreProcessed()
        {
            var enabledJobs = 5;
            var disabledJobs = 3;

            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(enabledJobs, Organization, enabled: true));
            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(disabledJobs, Organization, enabled: false));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

            var jobs = await _jobTriggerService.GetSyncJobsAsync();

            Assert.AreEqual(enabledJobs, jobs.Count);
        }


        [TestMethod]
        public async Task VerifyJobsWithNonexistentTargetGroupsAreErrored()
        {
            var enabledJobs = 5;
            var disabledJobs = 3;

            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(enabledJobs, Organization, enabled: true));
            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(disabledJobs, Organization, enabled: false));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

            foreach (var job in _syncJobRepository.Jobs.Take(enabledJobs))
            {
                var response = await _jobTriggerService.CanWriteToGroup(job);
                Assert.AreEqual(false, response);
            }
        }

        [TestMethod]
        public async Task VerifyJobsGMMDoesntOwnAreErrored()
        {
            var enabledJobs = 5;
            var disabledJobs = 3;

            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(enabledJobs, Organization, enabled: true));
            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(disabledJobs, Organization, enabled: false));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));

            foreach (var job in _syncJobRepository.Jobs.Take(enabledJobs))
            {
                var response = await _jobTriggerService.CanWriteToGroup(job);
                Assert.AreEqual(false, response);
            }
        }

        [TestMethod]
        public async Task VerifyJobsWithValidStartDateAreProcessed()
        {
            var validStartDateJobs = 5;
            var futureStartDateJobs = 3;

            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(validStartDateJobs, Organization, enabled: true));
            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(futureStartDateJobs, Organization, enabled: true, startDateBase: DateTime.UtcNow.AddDays(5)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

            var jobs = await _jobTriggerService.GetSyncJobsAsync();

            var jobsToProcessCount = _serviceBusTopicsRepository.Subscriptions.Sum(x => x.Value.Count);

            Assert.AreEqual(validStartDateJobs, jobs.Count);
        }

        [TestMethod]
        public async Task VerifyJobsWithValidPeriodsAreProcessed()
        {
            var jobsWithValidPeriods = 5;
            var jobsWithInvalidPeriods = 3;

            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(jobsWithValidPeriods, Organization, enabled: true));
            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(jobsWithInvalidPeriods, Organization, enabled: true, lastRunTime: DateTime.UtcNow.AddMinutes(30)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

            var jobs = await _jobTriggerService.GetSyncJobsAsync();

            var jobsToProcessCount = _serviceBusTopicsRepository.Subscriptions.Sum(x => x.Value.Count);

            Assert.AreEqual(jobsWithValidPeriods, jobs.Count);
        }

        [TestMethod]
        public async Task VerifyJobStatusIsUpdatedToInProgress()
        {
            var jobs = 2;

            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(jobs, Organization, enabled: true));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

            foreach (var job in _syncJobRepository.Jobs)
            {
                var canWriteToGroup = await _jobTriggerService.CanWriteToGroup(job);
                await _jobTriggerService.UpdateSyncJobStatusAsync(canWriteToGroup ? SyncStatus.InProgress : SyncStatus.NotOwnerOfDestinationGroup, job);
                Assert.AreEqual(job.Status, SyncStatus.InProgress.ToString());
            }
        }

        [TestMethod]
        public async Task VerifyJobStatusIsUpdatedToError()
        {
            var jobs = 2;

            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(jobs, Organization, enabled: true));

            foreach (var job in _syncJobRepository.Jobs)
            {
                var canWriteToGroup = await _jobTriggerService.CanWriteToGroup(job);
                await _jobTriggerService.UpdateSyncJobStatusAsync(canWriteToGroup ? SyncStatus.InProgress : SyncStatus.NotOwnerOfDestinationGroup, job);
                Assert.AreEqual(job.Status, SyncStatus.NotOwnerOfDestinationGroup.ToString());
            }
        }


        [TestMethod]
        public async Task VerifyUniqueMessageIdsAreCreated()
        {
            var MessageIdOne = "";
            var MessageIdTwo = "";

            var securityGroupJobCount = 1;
            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(securityGroupJobCount, SecurityGroup));

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
                new MockEmail<IEmailSenderRecipient>());

            var validStartDateJobs = 5;
            var futureStartDateJobs = 3;

            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(validStartDateJobs, Organization, enabled: true));
            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(futureStartDateJobs, Organization, enabled: true, startDateBase: DateTime.UtcNow.AddDays(5)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

            foreach (var job in _syncJobRepository.Jobs)
            {
                var groupName = await _graphGroupRepository.GetGroupNameAsync(job.TargetOfficeGroupId);
                await _jobTriggerService.SendEmailAsync(job, groupName);
            }
            var jobs = await _jobTriggerService.GetSyncJobsAsync();
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
                new MockEmail<IEmailSenderRecipient>());

            var validStartDateJobs = 5;
            var futureStartDateJobs = 3;

            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(validStartDateJobs, Organization, enabled: true));
            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(futureStartDateJobs, Organization, enabled: true, startDateBase: DateTime.UtcNow.AddDays(5)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

            foreach (var job in _syncJobRepository.Jobs)
            {
                await _jobTriggerService.SendEmailAsync(job, "GroupName");
            }
            var jobs = await _jobTriggerService.GetSyncJobsAsync();
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
                new MockEmail<IEmailSenderRecipient>());

            var validStartDateJobs = 5;
            var futureStartDateJobs = 3;

            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(validStartDateJobs, Organization, enabled: true));
            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(futureStartDateJobs, Organization, enabled: true, startDateBase: DateTime.UtcNow.AddDays(5)));

            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsThatExist.Add(x.TargetOfficeGroupId));
            _syncJobRepository.Jobs.ForEach(x => _graphGroupRepository.GroupsGMMOwns.Add(x.TargetOfficeGroupId));

            foreach (var job in _syncJobRepository.Jobs)
            {
                await _jobTriggerService.SendEmailAsync(job, "GroupName");
            }
            var jobs = await _jobTriggerService.GetSyncJobsAsync();
            Assert.AreEqual(validStartDateJobs, jobs.Count);
        }

        private List<SyncJob> CreateSampleSyncJobs(int numberOfJobs, string syncType, bool enabled = true, int period = 1, DateTime? startDateBase = null, DateTime? lastRunTime = null)
        {
            var jobs = new List<SyncJob>();

            for (int i = 0; i < numberOfJobs; i++)
            {
                var job = new SyncJob
                {
                    Enabled = enabled,
                    Requestor = $"requestor_{i}@email.com",
                    PartitionKey = DateTime.UtcNow.ToString("MMddyyyy"),
                    RowKey = Guid.NewGuid().ToString(),
                    Period = period,
                    Query = $"select * from users where id = '{i}'",
                    StartDate = startDateBase ?? DateTime.UtcNow.AddDays(-1),
                    Status = SyncStatus.Idle.ToString(),
                    TargetOfficeGroupId = Guid.NewGuid(),
                    Type = syncType,
                    LastRunTime = lastRunTime ?? DateTime.FromFileTimeUtc(0)
                };

                jobs.Add(job);
            }

            return jobs;
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
