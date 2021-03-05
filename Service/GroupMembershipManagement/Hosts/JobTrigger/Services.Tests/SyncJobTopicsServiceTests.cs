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

namespace Services.Tests
{
    [TestClass]
    public class SyncJobTopicsServiceTests
    {
        private SyncJobTopicsService _syncJobTopicsService = null;
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
            _syncJobTopicsService = new SyncJobTopicsService(_loggingRepository, _syncJobRepository, _serviceBusTopicsRepository, _graphGroupRepository, new MockKeyVaultSecret<ISyncJobTopicService>(), _mailRepository, new MockEmail<IEmailSenderRecipient>());
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

            await _syncJobTopicsService.ProcessSyncJobsAsync();

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

            await _syncJobTopicsService.ProcessSyncJobsAsync();

            var jobsToProcessCount = _serviceBusTopicsRepository.Subscriptions.Sum(x => x.Value.Count);

            Assert.AreEqual(enabledJobs, jobsToProcessCount);
        }


        [TestMethod]
        public async Task VerifyJobsWithNonexistentTargetGroupsAreErrored()
        {
            var enabledJobs = 5;
            var disabledJobs = 3;

            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(enabledJobs, Organization, enabled: true));
            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(disabledJobs, Organization, enabled: false));

            await _syncJobTopicsService.ProcessSyncJobsAsync();

            var jobsToProcessCount = _serviceBusTopicsRepository.Subscriptions.Sum(x => x.Value.Count);

            Assert.AreEqual(0, jobsToProcessCount);

            foreach (var job in _syncJobRepository.Jobs.Take(enabledJobs))
			{
                Assert.IsFalse(job.Enabled);
                Assert.AreEqual("Error", job.Status);
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

            await _syncJobTopicsService.ProcessSyncJobsAsync();

            var jobsToProcessCount = _serviceBusTopicsRepository.Subscriptions.Sum(x => x.Value.Count);

            Assert.AreEqual(0, jobsToProcessCount);

            foreach (var job in _syncJobRepository.Jobs.Take(enabledJobs))
			{
                Assert.IsFalse(job.Enabled);
                Assert.AreEqual("Error", job.Status);
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

            await _syncJobTopicsService.ProcessSyncJobsAsync();

            var jobsToProcessCount = _serviceBusTopicsRepository.Subscriptions.Sum(x => x.Value.Count);

            Assert.AreEqual(validStartDateJobs, jobsToProcessCount);
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

            await _syncJobTopicsService.ProcessSyncJobsAsync();

            var jobsToProcessCount = _serviceBusTopicsRepository.Subscriptions.Sum(x => x.Value.Count);

            Assert.AreEqual(jobsWithValidPeriods, jobsToProcessCount);
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

            await _syncJobTopicsService.ProcessSyncJobsAsync();

            foreach (var job in _syncJobRepository.Jobs)
            {
                var message = _serviceBusTopicsRepository.CreateMessage(job);
                MessageIdOne = message.MessageId;
                job.Status = SyncStatus.Idle.ToString();
            }

            await _syncJobTopicsService.ProcessSyncJobsAsync();

            foreach (var job in _syncJobRepository.Jobs)
            {
                var message = _serviceBusTopicsRepository.CreateMessage(job);
                MessageIdTwo = message.MessageId;
            }

            Assert.AreNotEqual(MessageIdOne, MessageIdTwo);
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
        }

        private class MockKeyVaultSecret<T> : IKeyVaultSecret<T>
		{
			public string Secret => "";
		}
	}
}
