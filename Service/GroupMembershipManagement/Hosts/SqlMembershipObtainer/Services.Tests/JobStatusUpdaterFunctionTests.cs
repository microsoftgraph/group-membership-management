// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.SyncJobHistory;
using Moq;
using Services.Contracts;
using Services.Tests.Helpers;
using SqlMembershipObtainer;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class JobStatusUpdaterFunctionTests
    {
        private Mock<ISyncJobStatusService> _syncJobStatusService;
        private SyncJob _syncJob;
        private SyncStatus? _capturedStatus;
        private SyncJobHistory _capturedHistory;
        private Guid _capturedSaveRunId;
        private Guid? _capturedSaveAdfRunId;

        [TestInitialize]
        public void Setup()
        {
            _syncJobStatusService = new Mock<ISyncJobStatusService>();
            _syncJob = SqlMembershipJobCreator.CreateSampleSyncJobs(1, "SqlMembership", 24).First();
            _syncJob.Status = SyncStatus.InProgress.ToString();
            _syncJob.RunId = Guid.NewGuid();

            _syncJobStatusService
                .Setup(x => x.UpdateJobStatusAsync(It.IsAny<SyncJob>(), It.IsAny<SyncStatus?>(), It.IsAny<SyncJobHistory>(), It.IsAny<string>()))
                .Callback<SyncJob, SyncStatus?, SyncJobHistory, string>((job, status, history, fn) =>
                {
                    _capturedStatus = status;
                    _capturedHistory = history;
                });

            _syncJobStatusService
                .Setup(x => x.SaveAdfRunIdAsync(It.IsAny<Guid>(), It.IsAny<Guid?>()))
                .Callback<Guid, Guid?>((runId, adfRunId) =>
                {
                    _capturedSaveRunId = runId;
                    _capturedSaveAdfRunId = adfRunId;
                })
                .ReturnsAsync(1);
        }

        [TestMethod]
        public async Task NullStatusPersistsAdfRunIdWithoutChangingJobStatusAsync()
        {
            var adfRunId = Guid.NewGuid();
            var request = new JobStatusUpdaterRequest
            {
                Status = null,
                SyncJob = _syncJob,
                CurrentPart = 1,
                TotalParts = 2,
                AdfRunId = adfRunId
            };

            var function = new JobStatusUpdaterFunction(NullLogger<JobStatusUpdaterFunction>.Instance, _syncJobStatusService.Object);
            await function.UpdateJobStatusAsync(request);

            // A metadata-only stash goes through the field-scoped upsert, NOT the generic status merge.
            _syncJobStatusService.Verify(
                x => x.SaveAdfRunIdAsync(_syncJob.RunId.Value, adfRunId), Times.Once,
                "A metadata-only stash must persist AdfRunId via the field-scoped update.");
            Assert.AreEqual(_syncJob.RunId.Value, _capturedSaveRunId);
            Assert.AreEqual(adfRunId, _capturedSaveAdfRunId);

            // It must NOT route through the status merge (which reads a replica and rewrites Status).
            _syncJobStatusService.Verify(
                x => x.UpdateJobStatusAsync(It.IsAny<SyncJob>(), It.IsAny<SyncStatus?>(), It.IsAny<SyncJobHistory>(), It.IsAny<string>()),
                Times.Never,
                "A metadata-only stash must not invoke the generic status/history merge.");

            // The in-memory job status must be left untouched.
            Assert.AreEqual(SyncStatus.InProgress.ToString(), _syncJob.Status);
        }

        [TestMethod]
        public async Task NullStatusWithoutRunIdDoesNotPersistAdfRunIdAsync()
        {
            _syncJob.RunId = null;
            var request = new JobStatusUpdaterRequest
            {
                Status = null,
                SyncJob = _syncJob,
                CurrentPart = 1,
                TotalParts = 2,
                AdfRunId = Guid.NewGuid()
            };

            var function = new JobStatusUpdaterFunction(NullLogger<JobStatusUpdaterFunction>.Instance, _syncJobStatusService.Object);
            await function.UpdateJobStatusAsync(request);

            // Without a RunId there is no history row to key on, so nothing must be written.
            _syncJobStatusService.Verify(
                x => x.SaveAdfRunIdAsync(It.IsAny<Guid>(), It.IsAny<Guid?>()), Times.Never,
                "A run with no RunId cannot be keyed, so no AdfRunId write must be attempted.");
            _syncJobStatusService.Verify(
                x => x.UpdateJobStatusAsync(It.IsAny<SyncJob>(), It.IsAny<SyncStatus?>(), It.IsAny<SyncJobHistory>(), It.IsAny<string>()),
                Times.Never);
        }

        [TestMethod]
        public async Task RealStatusUpdatesJobStatusAndHistoryAsync()
        {
            var adfRunId = Guid.NewGuid();
            var request = new JobStatusUpdaterRequest
            {
                Status = SyncStatus.Error,
                SyncJob = _syncJob,
                CurrentPart = 1,
                TotalParts = 2,
                AdfRunId = adfRunId
            };

            var function = new JobStatusUpdaterFunction(NullLogger<JobStatusUpdaterFunction>.Instance, _syncJobStatusService.Object);
            await function.UpdateJobStatusAsync(request);

            Assert.AreEqual(SyncStatus.Error, _capturedStatus, "A real status must flow through to the status service.");
            Assert.AreEqual(SyncStatus.Error.ToString(), _capturedHistory.Status);
            Assert.AreEqual(adfRunId, _capturedHistory.AdfRunId);
            Assert.IsNotNull(_capturedHistory.EndTime, "A terminal status must set an EndTime.");
            Assert.AreEqual(SyncStatus.Error.ToString(), _syncJob.Status);
        }
    }
}
