// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Models;
using Repositories.Contracts;
using Hosts.SyncJobUpdater;
using Services.Tests;
using Services.SyncJobUpdater.Tests.Mocks;

namespace Services.Tests
{
    [TestClass]
    public class SyncJobUpdaterServiceTests
    {
        private Mock<IDatabaseSyncJobsRepository> _mockDatabaseSyncJobsRepository;
        private MockLoggingRepository _mockLoggingRepository;
        private SyncJobUpdaterService _syncJobUpdaterService;

        [TestInitialize]
        public void Setup()
        {
            _mockDatabaseSyncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            _mockLoggingRepository = new MockLoggingRepository();
            _syncJobUpdaterService = new SyncJobUpdaterService(_mockDatabaseSyncJobsRepository.Object, _mockLoggingRepository);
        }

        [TestMethod]
        public async Task UpdateSyncJobStatus()
        {
            var syncJob = new SyncJob { Id = Guid.NewGuid() };
            var status = SyncStatus.Idle;
            await _syncJobUpdaterService.UpdateSyncJobStatusAsync(syncJob, status);
            _mockDatabaseSyncJobsRepository.Verify(repo => repo.UpdateSyncJobStatusAsync(It.Is<SyncJob[]>(jobs => jobs.Length == 1 && jobs[0] == syncJob), status), Times.Once);
        }
    }
}