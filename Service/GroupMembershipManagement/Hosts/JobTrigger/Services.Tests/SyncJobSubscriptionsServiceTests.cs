// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.ServiceBus;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Repositories.ServiceBusSubscriptions.Tests;
using Repositories.SyncJobs.Tests;
using Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Repositories.Mocks;
using MockSyncJobRepository = Repositories.SyncJobs.Tests.MockSyncJobRepository;

namespace Tests.Repositories
{
    [TestClass]
    public class SyncJobSubscriptionsServiceTests
    {

        private SyncJobSubscriptionsService _syncJobSubscriptionsService = null;
        private MockSyncJobRepository _syncJobRepository = null;
        private MockServiceBusSubscriptionsRepository _serviceBusSubscriptionsRepository = null;

        private const string Organization = "Organization";
        private const string SecurityGroup = "SecurityGroup";
        private const string TopicName = "syncjobs";

        [TestInitialize]
        public void InitializeTest()
        {
            _syncJobRepository = new MockSyncJobRepository();
            _serviceBusSubscriptionsRepository = new MockServiceBusSubscriptionsRepository();
            _syncJobSubscriptionsService = new SyncJobSubscriptionsService(_syncJobRepository, _serviceBusSubscriptionsRepository);
        }

        [TestMethod]
        public async Task ValidateJobsFromSpecifiedSubscriptionAreProcessed()
        {
            var organizationJobCount = 105;
            var securityGroupJobCount = 3;
            var jobs = new Dictionary<string, List<SyncJob>>
            {
                { Organization, CreateSampleSyncJobs(organizationJobCount, Organization, lastRunTime: DateTime.UtcNow.AddDays(-1)) },
                { SecurityGroup, CreateSampleSyncJobs(securityGroupJobCount, SecurityGroup, lastRunTime: DateTime.UtcNow.AddDays(-1)) }
            };

            AddSampleDataToSyncJobsTable(jobs);
            AddSampleDataToServiceBus(jobs);

            await _syncJobSubscriptionsService.ProcessSyncJobsAsync(TopicName, Organization);

            Assert.AreEqual(organizationJobCount, _syncJobRepository.Jobs.Count(x => x.Status == SyncStatus.InProgress.ToString()));
        }

        private List<SyncJob> CreateSampleSyncJobs(int numberOfJobs, string syncType, bool enabled = true, int period = 1, DateTime? startDateBase = null, DateTime? lastRunTime = null)
        {
            var jobs = new List<SyncJob>();

            for (int i = 0; i < numberOfJobs; i++)
            {
                var job = new SyncJob
                {
                    Requestor = $"requestor_{i}@email.com",
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

        private List<Message> CreateSampleServiceBusMessages(List<SyncJob> jobs)
        {
            var messages = new List<Message>();
            foreach(var job in jobs)
            {
                var message = new Message
                {
                    MessageId = $"{job.PartitionKey}_{job.RowKey}"
                };

                message.UserProperties.Add("PartitionKey", job.PartitionKey);
                message.UserProperties.Add("RowKey", job.RowKey);

                messages.Add(message);
            }

            return messages;
        }

        private void AddSampleDataToServiceBus(Dictionary<string, List<SyncJob>> jobs)
        {
            var topic = new MockTopic
            {
                Name = TopicName,
                Subscriptions = new List<MockSubscription>()
            };

            foreach (var job in jobs)
            {
                topic.Subscriptions.Add(new MockSubscription { Name = job.Key, Messages = CreateSampleServiceBusMessages(job.Value) });
            }

            _serviceBusSubscriptionsRepository.Topics = new List<MockTopic> { topic };
        }

        private void AddSampleDataToSyncJobsTable(Dictionary<string, List<SyncJob>> jobs)
        {
            foreach (var job in jobs)
            {
                _syncJobRepository.Jobs.AddRange(job.Value);
            }
        }
    }
}
