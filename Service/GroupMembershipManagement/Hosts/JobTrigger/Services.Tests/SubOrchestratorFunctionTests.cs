// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Hosts.JobTrigger;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class SubOrchestratorFunctionTests
    {
        Mock<IJobTriggerService> _jobTriggerService;
        Mock<ILoggingRepository> _loggingRespository;
        Mock<IDurableOrchestrationContext> _context;
        SyncStatus _syncStatus = SyncStatus.Idle;
        bool _canWriteToGroups;
        SyncJob _syncJob;
        SyncJobGroup _syncJobGroup;

        [TestInitialize]
        public void Setup()
        {
            _syncJob = CreateSyncJobs(1, "SecurityGroup").First();
            _canWriteToGroups = true;
            _jobTriggerService = new Mock<IJobTriggerService>();
            _loggingRespository = new Mock<ILoggingRepository>();
            _context = new Mock<IDurableOrchestrationContext>();

            _jobTriggerService.Setup(x => x.GroupExistsAndGMMCanWriteToGroupAsync(It.IsAny<SyncJob>())).ReturnsAsync(() => _canWriteToGroups);
            _jobTriggerService.Setup(x => x.GetGroupNameAsync(It.IsAny<Guid>())).ReturnsAsync(() => "Test Group");

            _context.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(JobStatusUpdaterFunction)), It.IsAny<JobStatusUpdaterRequest>()))
                    .Callback<string, object>(async (name, request) =>
                    {
                        var updateRequest = request as JobStatusUpdaterRequest;
                        await CallJobStatusUpdaterFunctionAsync(updateRequest);
                        _syncStatus = updateRequest.Status;
                    });

            _context.Setup(x => x.CallActivityAsync<bool>(It.Is<string>(x => x == nameof(GroupVerifierFunction)), It.IsAny<SyncJob>()))
                    .Returns(async () => await CallGroupVerifierFunctionAsync());

            _context.Setup(x => x.CallActivityAsync<SyncJobGroup>(It.Is<string>(x => x == nameof(GroupNameReaderFunction)), It.IsAny<SyncJob>()))
                    .Callback<string, object>(async (name, request) =>
                    {
                        _syncJobGroup = await CallGroupNameReaderFunctionAsync();
                    })
                    .ReturnsAsync(() => _syncJobGroup);

            _context.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(EmailSenderFunction)), It.IsAny<SyncJobGroup>()))
                     .Callback<string, object>(async (name, request) =>
                     {
                         await CallEmailSenderFunctionAsync();
                     });

            _context.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(TopicMessageSenderFunction)), It.IsAny<SyncJob>()))
                    .Callback<string, object>(async (name, request) =>
                    {
                        await CallTopicMessageSenderFunctionAsync();
                    });
        }

        [TestMethod]
        public async Task HandleInvalidJSONQuery()
        {
            var job = CreateSyncJobs(1, "SecurityGroup").First();
            job.Query = "{invalid json query}";

            _context.Setup(x => x.GetInput<SyncJob>()).Returns(job);

            var suborchrestrator = new SubOrchestratorFunction(_loggingRespository.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);

            _jobTriggerService.Verify(x => x.UpdateSyncJobStatusAsync(It.IsAny<SyncStatus>(), It.IsAny<SyncJob>()), Times.Once());
            _jobTriggerService.Verify(x => x.UpdateSyncJobStatusAsync(It.Is<SyncStatus>(s => s == SyncStatus.QueryNotValid), It.IsAny<SyncJob>()), Times.Once());

            _loggingRespository.Verify(x => x.LogMessageAsync(
                It.Is<LogMessage>(m => m.Message.Contains("JSON query is not valid")),
                It.IsAny<string>(),
                It.IsAny<string>()));
        }

        [TestMethod]
        public async Task HandleEmptyJSONQuery()
        {
            var job = CreateSyncJobs(1, "SecurityGroup").First();

            _context.Setup(x => x.GetInput<SyncJob>()).Returns(job);

            var suborchrestrator = new SubOrchestratorFunction(_loggingRespository.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);

            _jobTriggerService.Verify(x => x.UpdateSyncJobStatusAsync(It.IsAny<SyncStatus>(), It.IsAny<SyncJob>()), Times.Once());
            _jobTriggerService.Verify(x => x.UpdateSyncJobStatusAsync(It.Is<SyncStatus>(s => s == SyncStatus.QueryNotValid), It.IsAny<SyncJob>()), Times.Once());

            _loggingRespository.Verify(x => x.LogMessageAsync(
                It.Is<LogMessage>(m => m.Message.Contains("Job query is empty for job")),
                It.IsAny<string>(),
                It.IsAny<string>()));
        }

        [TestMethod]
        public async Task ProcessValidJSONQuery()
        {
            _syncJob.Query = GetJobQuery("SecurityGroup", new[] { Guid.NewGuid().ToString() });

            _context.Setup(x => x.GetInput<SyncJob>()).Returns(_syncJob);

            var suborchrestrator = new SubOrchestratorFunction(_loggingRespository.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);

            _loggingRespository.Verify(x => x.LogMessageAsync(
                It.Is<LogMessage>(m => !m.Message.Contains("JSON query is not valid") && !m.Message.Contains("Job query is empty for job")),
                It.IsAny<string>(),
                It.IsAny<string>()));

            _context.Verify(x => x.CallActivityAsync<SyncJobGroup>(It.Is<string>(x => x == nameof(GroupNameReaderFunction)), It.IsAny<SyncJob>()), Times.Once());
            _context.Verify(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(EmailSenderFunction)), It.IsAny<SyncJobGroup>()), Times.Once());
            _context.Verify(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(TopicMessageSenderFunction)), It.IsAny<SyncJob>()), Times.Once());

            _jobTriggerService.Verify(x => x.GetGroupNameAsync(It.IsAny<Guid>()), Times.Once());
            _jobTriggerService.Verify(x => x.SendEmailAsync(It.IsAny<SyncJob>(), It.IsAny<string>()), Times.Once());
            _jobTriggerService.Verify(x => x.SendMessageAsync(It.IsAny<SyncJob>()), Times.Once());
            _jobTriggerService.Verify(x => x.UpdateSyncJobStatusAsync(It.IsAny<SyncStatus>(), It.IsAny<SyncJob>()), Times.Once());
            _jobTriggerService.Verify(x => x.UpdateSyncJobStatusAsync(It.Is<SyncStatus>(s => s == SyncStatus.InProgress), It.IsAny<SyncJob>()), Times.Once());

            Assert.AreEqual(SyncStatus.InProgress, _syncStatus);
        }

        [TestMethod]
        public async Task DestinationGroupNotFound()
        {
            var job = CreateSyncJobs(1, "SecurityGroup").First();
            job.Query = GetJobQuery("SecurityGroup", new[] { Guid.NewGuid().ToString() });

            _context.Setup(x => x.GetInput<SyncJob>()).Returns(job);
            _context.Setup(x => x.CallActivityAsync<SyncJobGroup>(It.Is<string>(x => x == nameof(GroupNameReaderFunction)), It.IsAny<SyncJob>()))
                    .ReturnsAsync(new SyncJobGroup { Name = null, SyncJob = job });

            var suborchrestrator = new SubOrchestratorFunction(_loggingRespository.Object);
            await suborchrestrator.RunSubOrchestratorAsync(_context.Object);

            Assert.AreEqual(SyncStatus.DestinationGroupNotFound, _syncStatus);
        }

        private async Task CallJobStatusUpdaterFunctionAsync(JobStatusUpdaterRequest request)
        {
            var jobStatusUpdaterFunction = new JobStatusUpdaterFunction(_loggingRespository.Object, _jobTriggerService.Object);
            await jobStatusUpdaterFunction.UpdateJobStatusAsync(request);
        }

        private async Task<bool> CallGroupVerifierFunctionAsync()
        {
            var groupVerifierFunction = new GroupVerifierFunction(_loggingRespository.Object, _jobTriggerService.Object);
            return await groupVerifierFunction.VerifyGroupAsync(_syncJob);
        }

        private async Task<SyncJobGroup> CallGroupNameReaderFunctionAsync()
        {
            var groupNameReaderFunction = new GroupNameReaderFunction(_loggingRespository.Object, _jobTriggerService.Object);
            return await groupNameReaderFunction.GetGroupNameAsync(_syncJob);
        }

        private async Task CallEmailSenderFunctionAsync()
        {
            var emailSenderFunction = new EmailSenderFunction(_loggingRespository.Object, _jobTriggerService.Object);
            await emailSenderFunction.SendEmailAsync(_syncJobGroup);
        }

        private async Task CallTopicMessageSenderFunctionAsync()
        {
            var topicMessageSenderFunction = new TopicMessageSenderFunction(_loggingRespository.Object, _jobTriggerService.Object);
            await topicMessageSenderFunction.SendMessageAsync(_syncJob);
        }

        private List<SyncJob> CreateSyncJobs(int numberOfJobs, string syncType)
        {
            var jobs = new List<SyncJob>();

            for (int i = 0; i < numberOfJobs; i++)
            {
                var job = new SyncJob
                {
                    Requestor = $"requestor_{i}@email.com",
                    PartitionKey = DateTime.UtcNow.ToString("MMddyyyy"),
                    RowKey = Guid.NewGuid().ToString(),
                    Period = 6,
                    StartDate = DateTime.UtcNow.AddDays(-1),
                    Status = SyncStatus.Idle.ToString(),
                    TargetOfficeGroupId = Guid.NewGuid(),
                    LastRunTime = DateTime.FromFileTimeUtc(0)
                };

                jobs.Add(job);
            }

            return jobs;
        }

        private string GetJobQuery(string syncType, string[] groupIds)
        {
            string[] sourceGroups = groupIds.Select(x => $"'{x}'").ToArray();
            var query = $"[{{'type':'{syncType}','sources': [{string.Join(",", sourceGroups)}]}}]";
            return query;
        }
    }
}
