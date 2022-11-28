// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.AzureMaintenance;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.AzureMaintenance;
using Repositories.Contracts.InjectConfig;
using Services.Entities;
using Services.Entities.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

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

            var entities = new List<DynamicTableEntity>();
            for (int i = 0; i < 5; i++)
            {
                entities.Add(new DynamicTableEntity());
            }
            var backups = new List<BackupEntity>() { };

            azureTableBackupRepository.Setup(x => x.GetEntitiesAsync(It.IsAny<IAzureMaintenanceJob>()))
                                        .ReturnsAsync(entities);
            azureTableBackupRepository.Setup(x => x.BackupEntitiesAsync(backupSettings[0], entities))
                                        .ReturnsAsync(new BackupResult("backupTableName", "table", entities.Count));
            azureTableBackupRepository.Setup(x => x.GetBackupsAsync(backupSettings[0]))
                                        .ReturnsAsync(backups);
            azureTableBackupRepository.Setup(x => x.GetLastestBackupResultTrackerAsync(It.IsAny<IAzureMaintenanceJob>()))
                                        .ReturnsAsync((BackupResult) null);

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
                                                mailRespository.Object);
            await azureTableBackupService.RunBackupServiceAsync(backupSettings[0]);
            await azureTableBackupService.RunBackupServiceAsync(backupSettings[1]);

            azureTableBackupRepository.Verify(x => x.GetEntitiesAsync(It.IsAny<IAzureMaintenanceJob>()), Times.Exactly(2));
            azureTableBackupRepository.Verify(x => x.BackupEntitiesAsync(It.IsAny<IAzureMaintenanceJob>(), It.IsAny<List<DynamicTableEntity>>()), Times.Once());
            azureBlobBackupRepository.Verify(x => x.BackupEntitiesAsync(It.IsAny<IAzureMaintenanceJob>(), It.IsAny<List<DynamicTableEntity>>()), Times.Once());
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

            var entities = new List<DynamicTableEntity>();
            for (int i = 0; i < 5; i++)
            {
                entities.Add(new DynamicTableEntity());
            }
            var backups = new List<BackupEntity> { new BackupEntity("backupTableName", "blob") };

            azureTableBackupRepository.Setup(x => x.GetEntitiesAsync(It.IsAny<IAzureMaintenanceJob>()))
                                        .ReturnsAsync(entities);
            azureTableBackupRepository.Setup(x => x.BackupEntitiesAsync(backupSettings[0], entities))
                                        .ReturnsAsync(new BackupResult { BackupTableName = "backupTableName", BackedUpTo = "table", RowCount = entities.Count });
            azureTableBackupRepository.Setup(x => x.GetBackupsAsync(backupSettings[0]))
                                        .ReturnsAsync(backups);
            azureTableBackupRepository.Setup(x => x.GetLastestBackupResultTrackerAsync(It.IsAny<IAzureMaintenanceJob>()))
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
                                                mailRespository.Object);
            await azureTableBackupService.RunBackupServiceAsync(backupSettings[0]);
            await azureTableBackupService.RunBackupServiceAsync(backupSettings[1]);

            azureTableBackupRepository.Verify(x => x.GetEntitiesAsync(It.IsAny<IAzureMaintenanceJob>()), Times.Exactly(2));
            azureTableBackupRepository.Verify(x => x.BackupEntitiesAsync(It.IsAny<IAzureMaintenanceJob>(), It.IsAny<List<DynamicTableEntity>>()), Times.Once());
            azureBlobBackupRepository.Verify(x => x.BackupEntitiesAsync(It.IsAny<IAzureMaintenanceJob>(), It.IsAny<List<DynamicTableEntity>>()), Times.Once());
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

            var backups = new List<BackupEntity> { new BackupEntity("backupTableName", "blob") };

            azureTableBackupRepository.Setup(x => x.GetBackupsAsync(backupSettings[0]))
                                        .ReturnsAsync(backups);
            azureTableBackupRepository.Setup(x => x.GetLastestBackupResultTrackerAsync(It.IsAny<IAzureMaintenanceJob>()))
                                        .ReturnsAsync(new BackupResult { BackupTableName = "backupTableName", BackedUpTo = "table", RowCount = 1 });
            azureTableBackupRepository.Setup(x => x.GetBackupsAsync(backupSettings[1]))
                                        .ReturnsAsync(backups);

            var azureTableBackupService = new AzureMaintenanceService(loggerMock.Object,
                                                azureTableBackupRepository.Object,
                                                azureBlobBackupRepository.Object,
                                                syncJobRepository.Object,
                                                graphGroupRepository.Object,
                                                mailAddresses.Object,
                                                mailRespository.Object);
            await azureTableBackupService.RunBackupServiceAsync(backupSettings[0]);
            await azureTableBackupService.RunBackupServiceAsync(backupSettings[1]);

            azureTableBackupRepository.Verify(x => x.BackupEntitiesAsync(It.IsAny<IAzureMaintenanceJob>(), It.IsAny<List<DynamicTableEntity>>()), Times.Exactly(0));
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
                                                mailRespository.Object);
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
                                                mailRespository.Object);

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
    }
}
