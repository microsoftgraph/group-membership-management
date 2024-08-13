// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Models;
using Repositories.Contracts;
using Hosts.JobFinalizer;
using Services.Tests;
using Services.JobFinalizer.Tests.Mocks;

namespace Services.Tests
{
    [TestClass]
    public class JobFinalizerServiceTests
    {
        private Mock<IDatabaseSyncJobsRepository> _mockDatabaseSyncJobsRepository;
        private MockLoggingRepository _mockLoggingRepository;
        private JobFinalizerService _jobFinalizerService;

        [TestInitialize]
        public void Setup()
        {
            _mockDatabaseSyncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            _mockLoggingRepository = new MockLoggingRepository();
            _jobFinalizerService = new JobFinalizerService(_mockDatabaseSyncJobsRepository.Object, _mockLoggingRepository);
        }

        [TestMethod]
        public async Task UpdateSyncJobStatus()
        {
            var syncJob = new SyncJob { Id = Guid.NewGuid() };
            var status = SyncStatus.Idle;
            await _jobFinalizerService.UpdateSyncJobStatusAsync(syncJob, status);
            _mockDatabaseSyncJobsRepository.Verify(repo => repo.UpdateSyncJobStatusAsync(It.Is<SyncJob[]>(jobs => jobs.Length == 1 && jobs[0] == syncJob), status), Times.Once);
        }
    }
}