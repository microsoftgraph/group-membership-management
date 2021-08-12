// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.AzureTableBackup;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class AzureTableBackupServiceTests
    {
        [TestMethod]
        public async Task TestMissingAzureTableBackupSettings()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), It.IsAny<string>(), It.IsAny<string>()));

            var backupSettings = new List<IAzureTableBackup>();
            var azureTableBackupRepository = new Mock<IAzureTableBackupRepository>();
            var azureBlobBackupRepository = new Mock<IAzureStorageBackupRepository>();

            var azureTableBackupService = new AzureTableBackupService(backupSettings, loggerMock.Object, azureTableBackupRepository.Object, azureBlobBackupRepository.Object);
            await azureTableBackupService.BackupTablesAsync();

            azureTableBackupRepository.Verify(x => x.GetEntitiesAsync(It.IsAny<IAzureTableBackup>()), Times.Never());
            azureTableBackupRepository.Verify(x => x.BackupEntitiesAsync(It.IsAny<IAzureTableBackup>(), It.IsAny<List<DynamicTableEntity>>()), Times.Never());
            azureBlobBackupRepository.Verify(x => x.BackupEntitiesAsync(It.IsAny<IAzureTableBackup>(), It.IsAny<List<DynamicTableEntity>>()), Times.Never());
            azureBlobBackupRepository.Verify(x => x.DeleteBackupAsync(It.IsAny<IAzureTableBackup>(), It.IsAny<string>()), Times.Never());
        }

        [TestMethod]
        public async Task TestFirstBackup()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), It.IsAny<string>(), It.IsAny<string>()));

            var backupSettings = new List<IAzureTableBackup>()
            {
                new Services.Entities.AzureTableBackup("tableOne", "sourceConnection", "destinationConnection", "Table", 7),
                new Services.Entities.AzureTableBackup("tableOne", "sourceConnection", "destinationConnection", "Blob", 7)
            };

            var azureTableBackupRepository = new Mock<IAzureTableBackupRepository>();
            var azureBlobBackupRepository = new Mock<IAzureStorageBackupRepository>();
            var entities = new List<DynamicTableEntity>();
            for (int i = 0; i < 5; i++)
            {
                entities.Add(new DynamicTableEntity());
            }

            azureTableBackupRepository.Setup(x => x.GetEntitiesAsync(It.IsAny<IAzureTableBackup>()))
                                        .ReturnsAsync(entities);
            azureTableBackupRepository.Setup(x => x.BackupEntitiesAsync(backupSettings[0], entities))
                                        .ReturnsAsync(new BackupResult { BackupTableName = "backupTableName", BackedUpTo = "table", RowCount = entities.Count });
            azureTableBackupRepository.Setup(x => x.GetBackupsAsync(backupSettings[0]))
                                        .ReturnsAsync(new List<BackupEntity>());
            azureTableBackupRepository.Setup(x => x.GetLastestBackupResultTrackerAsync(It.IsAny<IAzureTableBackup>()))
                                        .ReturnsAsync((BackupResult)null);

            azureBlobBackupRepository.Setup(x => x.GetBackupsAsync(backupSettings[1]))
                                        .ReturnsAsync(new List<BackupEntity>());
            azureBlobBackupRepository.Setup(x => x.BackupEntitiesAsync(backupSettings[1], entities))
                                        .ReturnsAsync(new BackupResult("backupTableName", "blob", entities.Count));

            var azureTableBackupService = new AzureTableBackupService(backupSettings, loggerMock.Object, azureTableBackupRepository.Object, azureBlobBackupRepository.Object);
            await azureTableBackupService.BackupTablesAsync();

            azureTableBackupRepository.Verify(x => x.GetEntitiesAsync(It.IsAny<IAzureTableBackup>()), Times.Exactly(2));
            azureTableBackupRepository.Verify(x => x.BackupEntitiesAsync(It.IsAny<IAzureTableBackup>(), It.IsAny<List<DynamicTableEntity>>()), Times.Once());
            azureBlobBackupRepository.Verify(x => x.BackupEntitiesAsync(It.IsAny<IAzureTableBackup>(), It.IsAny<List<DynamicTableEntity>>()), Times.Once());
            azureBlobBackupRepository.Verify(x => x.DeleteBackupAsync(It.IsAny<IAzureTableBackup>(), It.IsAny<string>()), Times.Never());
        }

        [TestMethod]
        public async Task TestBackupWithExistingBackupTables()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), It.IsAny<string>(), It.IsAny<string>()));

            var backupSettings = new List<IAzureTableBackup>()
            {
                new Services.Entities.AzureTableBackup("tableOne", "sourceConnection", "destinationConnection", "Table", 7),
                new Services.Entities.AzureTableBackup("tableOne", "sourceConnection", "destinationConnection", "Blob", 7)
            };

            var azureTableBackupRepository = new Mock<IAzureTableBackupRepository>();
            var azureBlobBackupRepository = new Mock<IAzureStorageBackupRepository>();
            var entities = new List<DynamicTableEntity>();
            for (int i = 0; i < 5; i++)
            {
                entities.Add(new DynamicTableEntity());
            }

            azureTableBackupRepository.Setup(x => x.GetEntitiesAsync(It.IsAny<IAzureTableBackup>()))
                                        .ReturnsAsync(entities);
            azureTableBackupRepository.Setup(x => x.BackupEntitiesAsync(backupSettings[0], entities))
                                        .ReturnsAsync(new BackupResult { BackupTableName = "backupTableName", BackedUpTo = "table", RowCount = entities.Count });
            azureTableBackupRepository.Setup(x => x.GetBackupsAsync(backupSettings[0]))
                                        .ReturnsAsync(new List<BackupEntity> { new BackupEntity { Name = "backupTableName", StorageType = "table", CreatedDate = DateTime.UtcNow.AddDays(-7) } });
            azureTableBackupRepository.Setup(x => x.GetLastestBackupResultTrackerAsync(It.IsAny<IAzureTableBackup>()))
                                        .ReturnsAsync(new BackupResult { BackupTableName = "backupTableName", BackedUpTo = "table", RowCount = 1 });

            azureBlobBackupRepository.Setup(x => x.BackupEntitiesAsync(backupSettings[1], entities))
                                        .ReturnsAsync(new BackupResult { BackupTableName = "backupTableName", BackedUpTo = "blob", RowCount = entities.Count });
            azureBlobBackupRepository.Setup(x => x.GetBackupsAsync(backupSettings[1]))
                                        .ReturnsAsync(new List<BackupEntity> { new BackupEntity { Name = "backupTableName", StorageType = "blob", CreatedDate = DateTime.UtcNow.AddDays(-7) } });

            var azureTableBackupService = new AzureTableBackupService(backupSettings, loggerMock.Object, azureTableBackupRepository.Object, azureBlobBackupRepository.Object);
            await azureTableBackupService.BackupTablesAsync();

            azureTableBackupRepository.Verify(x => x.GetEntitiesAsync(It.IsAny<IAzureTableBackup>()), Times.Exactly(2));
            azureTableBackupRepository.Verify(x => x.BackupEntitiesAsync(It.IsAny<IAzureTableBackup>(), It.IsAny<List<DynamicTableEntity>>()), Times.Once());
            azureBlobBackupRepository.Verify(x => x.BackupEntitiesAsync(It.IsAny<IAzureTableBackup>(), It.IsAny<List<DynamicTableEntity>>()), Times.Once());
            azureBlobBackupRepository.Verify(x => x.DeleteBackupAsync(It.IsAny<IAzureTableBackup>(), It.IsAny<string>()), Times.Once());
        }
    }
}
