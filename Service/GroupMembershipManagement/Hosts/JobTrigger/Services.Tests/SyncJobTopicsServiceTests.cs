// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Repositories.SyncJobs.Tests;
using Tests.Repositories.Common;
using Tests.Repositories;

namespace Services.Tests
{
    [TestClass]
    public class SyncJobTopicsServiceTests
    {
        private SyncJobTopicsService _syncJobTopicsService = null;
        private MockSyncJobRepository _syncJobRepository = null;
        private MockLoggingRepository _loggingRepository = null;
        private MockServiceBusTopicsRepository _serviceBusTopicsRepository = null;

        private const string Organization = "Organization";
        private const string SecurityGroup = "SecurityGroup";

        [TestInitialize]
        public void InitializeTest()
        {
            _syncJobRepository = new MockSyncJobRepository();
            _loggingRepository = new MockLoggingRepository();
            _serviceBusTopicsRepository = new MockServiceBusTopicsRepository();
            _syncJobTopicsService = new SyncJobTopicsService(_loggingRepository, _syncJobRepository, _serviceBusTopicsRepository);
        }

        [TestMethod]
        public async Task ValidateJobsAreAddedToCorrectSubscription()
        {
            var organizationJobCount = 5;
            var securityGroupJobCount = 3;

            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(organizationJobCount, Organization));
            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(securityGroupJobCount, SecurityGroup));

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

            await _syncJobTopicsService.ProcessSyncJobsAsync();

            var jobsToProcessCount = _serviceBusTopicsRepository.Subscriptions.Sum(x => x.Value.Count);

            Assert.AreEqual(enabledJobs, jobsToProcessCount);
        }

        [TestMethod]
        public async Task VerifyJobsWithValidStartDateAreProcessed()
        {
            var validStartDateJobs = 5;
            var futureStartDateJobs = 3;

            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(validStartDateJobs, Organization, enabled: true));
            _syncJobRepository.Jobs.AddRange(CreateSampleSyncJobs(futureStartDateJobs, Organization, enabled: true, startDateBase: DateTime.UtcNow.AddDays(5)));

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

            await _syncJobTopicsService.ProcessSyncJobsAsync();

            var jobsToProcessCount = _serviceBusTopicsRepository.Subscriptions.Sum(x => x.Value.Count);

            Assert.AreEqual(jobsWithValidPeriods, jobsToProcessCount);
        }

        private List<SyncJob> CreateSampleSyncJobs(int numberOfJobs, string syncType, bool enabled = true, int period = 1, DateTime? startDateBase = null, DateTime? lastRunTime = null)
        {
            var jobs = new List<SyncJob>();

            for (int i = 0; i < numberOfJobs; i++)
            {
                var job = new SyncJob
                {
                    Enabled = enabled,
                    Owner = $"ownwer_{i}@email.com",
                    PartitionKey = DateTime.UtcNow.ToString("MMddyyyy"),
                    RowKey = Guid.NewGuid().ToString(),
                    Period = period,
                    Query = $"select * from users where id = '{i}'",
                    StartDate = startDateBase ?? DateTime.UtcNow.AddDays(-1),
                    Status = SyncStatus.Idle.ToString(),
                    TargetOfficeGroupId = Guid.NewGuid(),
                    Type = syncType,
                    LastRunTime = lastRunTime ?? DateTime.SpecifyKind(new DateTime(), DateTimeKind.Utc)
                };

                jobs.Add(job);
            }

            return jobs;
        }
    }
}

