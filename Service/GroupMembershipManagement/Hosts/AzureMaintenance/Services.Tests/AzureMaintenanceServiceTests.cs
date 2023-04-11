// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities.AzureMaintenance;
using Microsoft.Graph;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.AzureMaintenance;
using Repositories.Contracts.InjectConfig;
using Services.Entities;
using Services.Entities.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Models.AzureMaintenance;
using Azure.Data.Tables;

namespace Services.Tests
{
    [TestClass]
    public class AzureMaintenanceServiceTests
    {
        [TestMethod]
        public async Task TestFirstBackup()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));

            var backupSettings = new List<AzureMaintenanceJob>()
            {
                new AzureMaintenanceJob(
                    new StorageSetting("tableOne", "sourceConnection", StorageType.Table),
                    new StorageSetting("tableTwo", "destinationConnection", StorageType.Table),
                    true, true, 7),
                new AzureMaintenanceJob(
                    new StorageSetting("tableThree", "sourceConnection", StorageType.Table),
                    new StorageSetting("blobOne", "destinationConnection", StorageType.Blob),
                    true, true, 7)
            };

            var azureTableBackupRepository = new Mock<IAzureTableBackupRepository>();
            var azureBlobBackupRepository = new Mock<IAzureStorageBackupRepository>();
            var syncJobRepository = new Mock<ISyncJobRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var mailAddresses = new Mock<IEmailSenderRecipient>();
            var mailRespository = new Mock<IMailRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();

            var entities = new List<TableEntity>();
            for (int i = 0; i < 5; i++)
            {
                entities.Add(new TableEntity());
            }
            var backups = new List<BackupEntity>() { };

            azureTableBackupRepository.Setup(x => x.GetEntitiesAsync(It.IsAny<IAzureMaintenanceJob>()))
                                        .ReturnsAsync(entities);
            azureTableBackupRepository.Setup(x => x.BackupEntitiesAsync(backupSettings[0], entities))
                                        .ReturnsAsync(new BackupResult("backupTableName", "table", entities.Count));
            azureTableBackupRepository.Setup(x => x.GetBackupsAsync(backupSettings[0]))
                                        .ReturnsAsync(backups);
            azureTableBackupRepository.Setup(x => x.GetLatestBackupResultTrackerAsync(It.IsAny<IAzureMaintenanceJob>()))
                                        .ReturnsAsync((BackupResult)null);

            azureBlobBackupRepository.Setup(x => x.GetBackupsAsync(backupSettings[1]))
                                        .ReturnsAsync(backups);
            azureBlobBackupRepository.Setup(x => x.BackupEntitiesAsync(backupSettings[1], entities))
                                        .ReturnsAsync(new BackupResult("backupTableName", "blob", entities.Count));

            var azureTableBackupService = new AzureMaintenanceService(loggerMock.Object,
                                                azureTableBackupRepository.Object,
                                                azureBlobBackupRepository.Object,
                                                syncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                mailAddresses.Object,
                                                mailRespository.Object,
                                                handleInactiveJobsConfig.Object);
            await azureTableBackupService.RunBackupServiceAsync(backupSettings[0]);
            await azureTableBackupService.RunBackupServiceAsync(backupSettings[1]);

            azureTableBackupRepository.Verify(x => x.GetEntitiesAsync(It.IsAny<IAzureMaintenanceJob>()), Times.Exactly(2));
            azureTableBackupRepository.Verify(x => x.BackupEntitiesAsync(It.IsAny<IAzureMaintenanceJob>(), It.IsAny<List<TableEntity>>()), Times.Once());
            azureBlobBackupRepository.Verify(x => x.BackupEntitiesAsync(It.IsAny<IAzureMaintenanceJob>(), It.IsAny<List<TableEntity>>()), Times.Once());
        }

        [TestMethod]
        public async Task TestBackupWithExistingBackupTables()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));

            var backupSettings = new List<AzureMaintenanceJob>()
            {
                new AzureMaintenanceJob(
                    new StorageSetting("tableOne", "sourceConnection", StorageType.Table),
                    new StorageSetting("tableTwo", "destinationConnection", StorageType.Table),
                    true, true, 7),
                new AzureMaintenanceJob(
                    new StorageSetting("tableThree", "sourceConnection", StorageType.Table),
                    new StorageSetting("blobOne", "destinationConnection", StorageType.Blob),
                    true, true, 7)
            };

            var azureTableBackupRepository = new Mock<IAzureTableBackupRepository>();
            var azureBlobBackupRepository = new Mock<IAzureStorageBackupRepository>();
            var syncJobRepository = new Mock<ISyncJobRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var mailAddresses = new Mock<IEmailSenderRecipient>();
            var mailRespository = new Mock<IMailRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();

            var entities = new List<TableEntity>();
            for (int i = 0; i < 5; i++)
            {
                entities.Add(new TableEntity());
            }
            var backups = new List<BackupEntity> { new BackupEntity("backupTableName", "blob") };

            azureTableBackupRepository.Setup(x => x.GetEntitiesAsync(It.IsAny<IAzureMaintenanceJob>()))
                                        .ReturnsAsync(entities);
            azureTableBackupRepository.Setup(x => x.BackupEntitiesAsync(backupSettings[0], entities))
                                        .ReturnsAsync(new BackupResult { BackupTableName = "backupTableName", BackedUpTo = "table", RowCount = entities.Count });
            azureTableBackupRepository.Setup(x => x.GetBackupsAsync(backupSettings[0]))
                                        .ReturnsAsync(backups);
            azureTableBackupRepository.Setup(x => x.GetLatestBackupResultTrackerAsync(It.IsAny<IAzureMaintenanceJob>()))
                                        .ReturnsAsync(new BackupResult { BackupTableName = "backupTableName", BackedUpTo = "table", RowCount = 1 });

            azureBlobBackupRepository.Setup(x => x.BackupEntitiesAsync(backupSettings[1], entities))
                                        .ReturnsAsync(new BackupResult { BackupTableName = "backupTableName", BackedUpTo = "blob", RowCount = entities.Count });
            azureBlobBackupRepository.Setup(x => x.GetBackupsAsync(backupSettings[1]))
                                        .ReturnsAsync(backups);

            var azureTableBackupService = new AzureMaintenanceService(loggerMock.Object,
                                                azureTableBackupRepository.Object,
                                                azureBlobBackupRepository.Object,
                                                syncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                mailAddresses.Object,
                                                mailRespository.Object,
                                                handleInactiveJobsConfig.Object);
            await azureTableBackupService.RunBackupServiceAsync(backupSettings[0]);
            await azureTableBackupService.RunBackupServiceAsync(backupSettings[1]);

            azureTableBackupRepository.Verify(x => x.GetEntitiesAsync(It.IsAny<IAzureMaintenanceJob>()), Times.Exactly(2));
            azureTableBackupRepository.Verify(x => x.BackupEntitiesAsync(It.IsAny<IAzureMaintenanceJob>(), It.IsAny<List<TableEntity>>()), Times.Once());
            azureBlobBackupRepository.Verify(x => x.BackupEntitiesAsync(It.IsAny<IAzureMaintenanceJob>(), It.IsAny<List<TableEntity>>()), Times.Once());
        }

        [TestMethod]
        public async Task TestCleanupOnlyForTables()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));

            var backupSettings = new List<AzureMaintenanceJob>()
            {
                new AzureMaintenanceJob(
                    new StorageSetting("tableOne", "sourceConnection", StorageType.Table),
                    new StorageSetting("tableTwo", "destinationConnection", StorageType.Table),
                    true, true, 7),
                new AzureMaintenanceJob(
                    new StorageSetting("tableThree", "sourceConnection", StorageType.Table),
                    new StorageSetting("tableFour", "destinationConnection", StorageType.Blob),
                    true, true, 7)
            };

            var azureTableBackupRepository = new Mock<IAzureTableBackupRepository>();
            var azureBlobBackupRepository = new Mock<IAzureStorageBackupRepository>();
            var syncJobRepository = new Mock<ISyncJobRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var mailAddresses = new Mock<IEmailSenderRecipient>();
            var mailRespository = new Mock<IMailRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();

            var backups = new List<BackupEntity> { new BackupEntity("backupTableName", "blob") };

            azureTableBackupRepository.Setup(x => x.GetBackupsAsync(backupSettings[0]))
                                        .ReturnsAsync(backups);
            azureTableBackupRepository.Setup(x => x.GetLatestBackupResultTrackerAsync(It.IsAny<IAzureMaintenanceJob>()))
                                        .ReturnsAsync(new BackupResult { BackupTableName = "backupTableName", BackedUpTo = "table", RowCount = 1 });
            azureTableBackupRepository.Setup(x => x.GetBackupsAsync(backupSettings[1]))
                                        .ReturnsAsync(backups);

            var azureTableBackupService = new AzureMaintenanceService(loggerMock.Object,
                                                azureTableBackupRepository.Object,
                                                azureBlobBackupRepository.Object,
                                                syncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                mailAddresses.Object,
                                                mailRespository.Object,
                                                handleInactiveJobsConfig.Object);
            await azureTableBackupService.RunBackupServiceAsync(backupSettings[0]);
            await azureTableBackupService.RunBackupServiceAsync(backupSettings[1]);

            azureTableBackupRepository.Verify(x => x.BackupEntitiesAsync(It.IsAny<IAzureMaintenanceJob>(), It.IsAny<List<TableEntity>>()), Times.Exactly(0));
        }

        [TestMethod]
        public async Task TestBackupRetrieval()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));

            var tableSource = "tableOne";
            var blobSource = "blobTwo";

            var backupSettings = new List<AzureMaintenanceJob>()
            {
                new AzureMaintenanceJob(
                    new StorageSetting("tableOne", "sourceConnection", StorageType.Table),
                    new StorageSetting("tableTwo", "destinationConnection", StorageType.Table),
                    true, true, 7),
                new AzureMaintenanceJob(
                    new StorageSetting("blobOne", "sourceConnection", StorageType.Blob),
                    new StorageSetting("blobTwo", "destinationConnection", StorageType.Blob),
                    true, true, 7)
            };

            var azureTableBackupRepository = new Mock<IAzureTableBackupRepository>();
            var azureBlobBackupRepository = new Mock<IAzureStorageBackupRepository>();
            var syncJobRepository = new Mock<ISyncJobRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var mailAddresses = new Mock<IEmailSenderRecipient>();
            var mailRespository = new Mock<IMailRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();

            var backupsTable = new List<BackupEntity> { new BackupEntity(tableSource, "table") };
            var backupsBlob = new List<BackupEntity> { new BackupEntity(blobSource, "blob") };

            azureTableBackupRepository.Setup(x => x.GetBackupsAsync(backupSettings[0]))
                                        .ReturnsAsync(backupsTable);
            azureBlobBackupRepository.Setup(x => x.GetBackupsAsync(backupSettings[1]))
                                        .ReturnsAsync(backupsBlob);

            var azureTableBackupService = new AzureMaintenanceService(loggerMock.Object,
                                                azureTableBackupRepository.Object,
                                                azureBlobBackupRepository.Object,
                                                syncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                mailAddresses.Object,
                                                mailRespository.Object,
                                                handleInactiveJobsConfig.Object);
            var requests1 = await azureTableBackupService.RetrieveBackupsAsync(backupSettings[0]);
            var requests2 = await azureTableBackupService.RetrieveBackupsAsync(backupSettings[1]);

            azureTableBackupRepository.Verify(x => x.GetBackupsAsync(It.IsAny<IAzureMaintenanceJob>()), Times.Once());
            azureBlobBackupRepository.Verify(x => x.GetBackupsAsync(It.IsAny<IAzureMaintenanceJob>()), Times.Once());

            Assert.AreEqual(requests1.Count, 1);
            Assert.AreEqual(requests1[0].TargetName, tableSource);
            Assert.AreEqual(requests2.Count, 1);
            Assert.AreEqual(requests2[0].TargetName, blobSource);
        }

        [TestMethod]
        public async Task TestReviewAndDelete()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));

            var tableSource = "tableOne";
            var blobSource = "blobTwo";

            var backupSettings = new List<AzureMaintenanceJob>()
            {
                new AzureMaintenanceJob(
                    new StorageSetting("tableOne", "sourceConnection", StorageType.Table),
                    new StorageSetting("tableTwo", "destinationConnection", StorageType.Table),
                    true, true, 7),
                new AzureMaintenanceJob(
                    new StorageSetting("blobOne", "sourceConnection", StorageType.Blob),
                    new StorageSetting("blobTwo", "destinationConnection", StorageType.Blob),
                    true, true, 7)
            };

            var azureTableBackupRepository = new Mock<IAzureTableBackupRepository>();
            var azureBlobBackupRepository = new Mock<IAzureStorageBackupRepository>();
            var syncJobRepository = new Mock<ISyncJobRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var mailAddresses = new Mock<IEmailSenderRecipient>();
            var mailRespository = new Mock<IMailRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();

            var backupsTable = new List<BackupEntity> { new BackupEntity(tableSource, "table") };
            var backupsBlob = new List<BackupEntity> { new BackupEntity(blobSource, "blob") };

            azureTableBackupRepository.Setup(x => x.GetBackupsAsync(backupSettings[0]))
                                        .ReturnsAsync(backupsTable);
            azureTableBackupRepository.Setup(x => x.VerifyCleanupAsync(backupSettings[0], tableSource))
                                        .ReturnsAsync(true);

            azureBlobBackupRepository.Setup(x => x.GetBackupsAsync(backupSettings[1]))
                                        .ReturnsAsync(backupsBlob);
            azureBlobBackupRepository.Setup(x => x.VerifyCleanupAsync(backupSettings[1], blobSource))
                                        .ReturnsAsync(false);

            var azureTableBackupService = new AzureMaintenanceService(loggerMock.Object,
                                                azureTableBackupRepository.Object,
                                                azureBlobBackupRepository.Object,
                                                syncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                mailAddresses.Object,
                                                mailRespository.Object,
                                                handleInactiveJobsConfig.Object);

            var tableRequest = await azureTableBackupService.RetrieveBackupsAsync(backupSettings[0]);
            var blobRequest = await azureTableBackupService.RetrieveBackupsAsync(backupSettings[1]);
            var tableResponse = await azureTableBackupService.ReviewAndDeleteAsync(tableRequest[0].MaintenanceSetting, tableRequest[0].TargetName);
            var blobResponse = await azureTableBackupService.ReviewAndDeleteAsync(blobRequest[0].MaintenanceSetting, blobRequest[0].TargetName);

            Assert.IsTrue(tableResponse);
            Assert.IsFalse(blobResponse);

            azureTableBackupRepository.Verify(x => x.CleanupAsync(It.IsAny<IAzureMaintenanceJob>(), It.IsAny<string>()), Times.Once());
            azureTableBackupRepository.Verify(x => x.DeleteBackupTrackersAsync(It.IsAny<IAzureMaintenanceJob>(), It.IsAny<List<(string, string)>>()), Times.Once());
            azureBlobBackupRepository.Verify(x => x.CleanupAsync(It.IsAny<IAzureMaintenanceJob>(), It.IsAny<string>()), Times.Exactly(0));
        }

        [TestMethod]
        public async Task TestBackupInactiveJobs()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));
            var jobs = new List<SyncJob>();

            for (int i = 0; i < 2; i++)
            {
                var job = new SyncJob
                {
                    Requestor = $"requestor_{i}@email.com",
                    PartitionKey = DateTime.UtcNow.ToString("MMddyyyy"),
                    RowKey = Guid.NewGuid().ToString(),
                    Period = 6,
                    Query = "[{ \"type\": \"SecurityGroup\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                    StartDate = DateTime.UtcNow.AddDays(-1),
                    Status = SyncStatus.CustomerPaused.ToString(),
                    TargetOfficeGroupId = Guid.NewGuid(),
                    LastRunTime = DateTime.FromFileTimeUtc(0),
                    RunId = Guid.NewGuid()
                };

                jobs.Add(job);
            }

            var azureTableBackupRepository = new Mock<IAzureTableBackupRepository>();
            var azureBlobBackupRepository = new Mock<IAzureStorageBackupRepository>();
            var syncJobRepository = new Mock<ISyncJobRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var mailAddresses = new Mock<IEmailSenderRecipient>();
            var mailRespository = new Mock<IMailRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();

            azureTableBackupRepository.Setup(x => x.BackupInactiveJobsAsync(jobs)).ReturnsAsync(2);

            var azureTableBackupService = new AzureMaintenanceService(loggerMock.Object,
                                                azureTableBackupRepository.Object,
                                                azureBlobBackupRepository.Object,
                                                syncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                mailAddresses.Object,
                                                mailRespository.Object,
                                                handleInactiveJobsConfig.Object);

            var countOfBackedUpJobs = await azureTableBackupService.BackupInactiveJobsAsync(jobs);
            Assert.AreEqual(countOfBackedUpJobs, jobs.Count);
            azureTableBackupRepository.Verify(x => x.BackupInactiveJobsAsync(It.IsAny<List<SyncJob>>()), Times.Once());

            jobs = new List<SyncJob>();
            countOfBackedUpJobs = await azureTableBackupService.BackupInactiveJobsAsync(jobs);
            Assert.AreEqual(countOfBackedUpJobs, 0);
        }

        [TestMethod]
        public async Task TestRemoveBackups()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));

            var tables = new List<BackupTable>();

            for (int i = 0; i < 2; i++)
            {
                var table = new BackupTable
                {
                    TableName = $"Inactive{i}",
                    CreatedDate = DateTime.UtcNow.AddDays(-35)
                };

                tables.Add(table);
            }

            var azureTableBackupRepository = new Mock<IAzureTableBackupRepository>();
            var azureBlobBackupRepository = new Mock<IAzureStorageBackupRepository>();
            var syncJobRepository = new Mock<ISyncJobRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var mailAddresses = new Mock<IEmailSenderRecipient>();
            var mailRespository = new Mock<IMailRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();

            azureTableBackupRepository.Setup(x => x.GetInactiveBackupsAsync()).ReturnsAsync(tables);

            var azureTableBackupService = new AzureMaintenanceService(loggerMock.Object,
                                                azureTableBackupRepository.Object,
                                                azureBlobBackupRepository.Object,
                                                syncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                mailAddresses.Object,
                                                mailRespository.Object,
                                                handleInactiveJobsConfig.Object);

            var countOfRemovedBackUps = await azureTableBackupService.RemoveBackupsAsync();
            Assert.AreEqual(countOfRemovedBackUps.Count, tables.Count);
            azureTableBackupRepository.Verify(x => x.GetInactiveBackupsAsync(), Times.Once());
            azureTableBackupRepository.Verify(x => x.DeleteBackupTableAsync(It.IsAny<string>()), Times.Exactly(2));
        }

        [TestMethod]
        public async Task TestRemoveInactiveJobs()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));

            var jobs = new List<SyncJob>();

            for (int i = 0; i < 2; i++)
            {
                var job = new SyncJob
                {
                    Requestor = $"requestor_{i}@email.com",
                    PartitionKey = DateTime.UtcNow.ToString("MMddyyyy"),
                    RowKey = Guid.NewGuid().ToString(),
                    Period = 6,
                    Query = "[{ \"type\": \"SecurityGroup\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                    StartDate = DateTime.UtcNow.AddDays(-1),
                    Status = SyncStatus.CustomerPaused.ToString(),
                    TargetOfficeGroupId = Guid.NewGuid(),
                    LastRunTime = DateTime.FromFileTimeUtc(0),
                    RunId = Guid.NewGuid()
                };

                jobs.Add(job);
            }

            var j = GetJobs(jobs);

            var azureTableBackupRepository = new Mock<IAzureTableBackupRepository>();
            var azureBlobBackupRepository = new Mock<IAzureStorageBackupRepository>();
            var syncJobRepository = new Mock<ISyncJobRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var mailAddresses = new Mock<IEmailSenderRecipient>();
            var mailRespository = new Mock<IMailRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();

            var azureTableBackupService = new AzureMaintenanceService(loggerMock.Object,
                                                azureTableBackupRepository.Object,
                                                azureBlobBackupRepository.Object,
                                                syncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                mailAddresses.Object,
                                                mailRespository.Object,
                                                handleInactiveJobsConfig.Object);

            await azureTableBackupService.RemoveInactiveJobsAsync(j);
            syncJobRepository.Verify(x => x.DeleteSyncJobsAsync(It.IsAny<List<SyncJob>>()), Times.Once());
        }

        [TestMethod]
        public async Task TestGetGroupName()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));
            var azureTableBackupRepository = new Mock<IAzureTableBackupRepository>();
            var azureBlobBackupRepository = new Mock<IAzureStorageBackupRepository>();
            var syncJobRepository = new Mock<ISyncJobRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var mailAddresses = new Mock<IEmailSenderRecipient>();
            var mailRespository = new Mock<IMailRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();

            graphGroupRepository.Setup(x => x.GetGroupNameAsync(It.IsAny<Guid>())).ReturnsAsync(() => "Test Group");

            var azureTableBackupService = new AzureMaintenanceService(loggerMock.Object,
                                                azureTableBackupRepository.Object,
                                                azureBlobBackupRepository.Object,
                                                syncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                mailAddresses.Object,
                                                mailRespository.Object,
                                                handleInactiveJobsConfig.Object);

            await azureTableBackupService.GetGroupNameAsync(Guid.NewGuid());
            graphGroupRepository.Verify(x => x.GetGroupNameAsync(It.IsAny<Guid>()), Times.Once());
        }

        [TestMethod]
        public async Task TestGetSyncJobs()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));

            var job = GetJob();

            var azureTableBackupRepository = new Mock<IAzureTableBackupRepository>();
            var azureBlobBackupRepository = new Mock<IAzureStorageBackupRepository>();
            var syncJobRepository = new Mock<ISyncJobRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var mailAddresses = new Mock<IEmailSenderRecipient>();
            var mailRespository = new Mock<IMailRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();

            var azureTableBackupService = new AzureMaintenanceService(loggerMock.Object,
                                                azureTableBackupRepository.Object,
                                                azureBlobBackupRepository.Object,
                                                syncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                mailAddresses.Object,
                                                mailRespository.Object,
                                                handleInactiveJobsConfig.Object);

            var jobs = await azureTableBackupService.GetSyncJobsAsync();
            Assert.AreEqual(jobs.Count, 0);
            syncJobRepository.Verify(x => x.GetSpecificSyncJobsAsync(), Times.Once());

            syncJobRepository.Setup(x => x.GetSpecificSyncJobsAsync()).Returns(job);
            jobs = await azureTableBackupService.GetSyncJobsAsync();
            Assert.AreEqual(jobs.Count, 1);
            syncJobRepository.Verify(x => x.GetSpecificSyncJobsAsync(), Times.Exactly(2));
        }

        [TestMethod]
        public async Task TestSendEmail()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));

            var job = new SyncJob
            {
                Requestor = $"requestor@email.com",
                PartitionKey = DateTime.UtcNow.ToString("MMddyyyy"),
                RowKey = Guid.NewGuid().ToString(),
                Period = 6,
                Query = "[{ \"type\": \"SecurityGroup\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                StartDate = DateTime.UtcNow.AddDays(-1),
                Status = SyncStatus.CustomerPaused.ToString(),
                TargetOfficeGroupId = Guid.NewGuid(),
                LastRunTime = DateTime.FromFileTimeUtc(0),
                RunId = Guid.NewGuid()
            };

            var users = new List<User>();

            for (int i = 0; i < 2; i++)
            {
                var user = new User
                {
                    Mail = $"requestor_{i}@email.com"
                };

                users.Add(user);
            }

            var azureTableBackupRepository = new Mock<IAzureTableBackupRepository>();
            var azureBlobBackupRepository = new Mock<IAzureStorageBackupRepository>();
            var syncJobRepository = new Mock<ISyncJobRepository>();
            var graphGroupRepository = new Mock<IGraphGroupRepository>();
            var mailAddresses = new Mock<IEmailSenderRecipient>();
            var mailRespository = new Mock<IMailRepository>();
            var handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();

            _ = graphGroupRepository.Setup(x => x.GetGroupOwnersAsync(job.TargetOfficeGroupId, 0)).ReturnsAsync(users);

            var azureTableBackupService = new AzureMaintenanceService(loggerMock.Object,
                                                azureTableBackupRepository.Object,
                                                azureBlobBackupRepository.Object,
                                                syncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                mailAddresses.Object,
                                                mailRespository.Object,
                                                handleInactiveJobsConfig.Object);

            await azureTableBackupService.SendEmailAsync(job, "Test Group");
            mailRespository.Verify(x => x.SendMailAsync(It.IsAny<EmailMessage>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Once());
        }

        public IEnumerable<SyncJob> GetJobs(List<SyncJob> jobs)
        {
            List<SyncJob> j = jobs;
            return j;
        }

        static async IAsyncEnumerable<SyncJob> GetJob()
        {
            yield return await Task.FromResult(new SyncJob());
        }
    }
}
