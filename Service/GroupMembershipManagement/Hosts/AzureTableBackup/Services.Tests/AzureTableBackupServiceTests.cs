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

            var azureTableBackupService = new AzureTableBackupService(backupSettings, loggerMock.Object, azureTableBackupRepository.Object);
            await azureTableBackupService.BackupTablesAsync();

            azureTableBackupRepository.Verify(x => x.GetEntitiesAsync(It.IsAny<IAzureTableBackup>()), Times.Never());
            azureTableBackupRepository.Verify(x => x.BackupEntitiesAsync(It.IsAny<IAzureTableBackup>(), It.IsAny<List<DynamicTableEntity>>()), Times.Never());
        }

        [TestMethod]
        public async Task TestFirstBackup()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), It.IsAny<string>(), It.IsAny<string>()));

            var backupSettings = new List<IAzureTableBackup>()
            {
                new Services.Entities.AzureTableBackup("tableOne", "sourceConnection", "destinationConnection", 7)
            };

            var azureTableBackupRepository = new Mock<IAzureTableBackupRepository>();
            var entities = new List<DynamicTableEntity>();
            for (int i = 0; i < 5; i++)
            {
                entities.Add(new DynamicTableEntity());
            }

            azureTableBackupRepository.Setup(x => x.GetEntitiesAsync(It.IsAny<IAzureTableBackup>()))
                                        .ReturnsAsync(entities);
            azureTableBackupRepository.Setup(x => x.BackupEntitiesAsync(backupSettings[0], entities))
                                        .ReturnsAsync(new BackupResult { BackupTableName = "backupTableName", RowCount = entities.Count });
            azureTableBackupRepository.Setup(x => x.GetBackupTablesAsync(backupSettings[0]))
                                        .ReturnsAsync(new List<BackupTable>());
            azureTableBackupRepository.Setup(x => x.GetLastestBackupResultTrackerAsync(It.IsAny<IAzureTableBackup>()))
                                        .ReturnsAsync((BackupResult)null);

            var azureTableBackupService = new AzureTableBackupService(backupSettings, loggerMock.Object, azureTableBackupRepository.Object);
            await azureTableBackupService.BackupTablesAsync();

            azureTableBackupRepository.Verify(x => x.GetEntitiesAsync(It.IsAny<IAzureTableBackup>()), Times.Once());
            azureTableBackupRepository.Verify(x => x.BackupEntitiesAsync(It.IsAny<IAzureTableBackup>(), It.IsAny<List<DynamicTableEntity>>()), Times.Once());
        }

        [TestMethod]
        public async Task TestBackupWithExistingBackupTables()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), It.IsAny<string>(), It.IsAny<string>()));

            var backupSettings = new List<IAzureTableBackup>()
            {
                new Services.Entities.AzureTableBackup("tableOne", "sourceConnection", "destinationConnection", 7)
            };

            var azureTableBackupRepository = new Mock<IAzureTableBackupRepository>();
            var entities = new List<DynamicTableEntity>();
            for (int i = 0; i < 5; i++)
            {
                entities.Add(new DynamicTableEntity());
            }

            azureTableBackupRepository.Setup(x => x.GetEntitiesAsync(It.IsAny<IAzureTableBackup>()))
                                        .ReturnsAsync(entities);
            azureTableBackupRepository.Setup(x => x.BackupEntitiesAsync(backupSettings[0], entities))
                                        .ReturnsAsync(new BackupResult { BackupTableName = "backupTableName", RowCount = entities.Count });
            azureTableBackupRepository.Setup(x => x.GetBackupTablesAsync(backupSettings[0]))
                                        .ReturnsAsync(new List<BackupTable> { new BackupTable { TableName = "backupTableName", TimeStamp = new DateTimeOffset(DateTime.UtcNow).AddDays(-7) } });
            azureTableBackupRepository.Setup(x => x.GetLastestBackupResultTrackerAsync(It.IsAny<IAzureTableBackup>()))
                                        .ReturnsAsync(new BackupResult { BackupTableName = "backupTableName", RowCount = 1 });

            var azureTableBackupService = new AzureTableBackupService(backupSettings, loggerMock.Object, azureTableBackupRepository.Object);
            await azureTableBackupService.BackupTablesAsync();

            azureTableBackupRepository.Verify(x => x.GetEntitiesAsync(It.IsAny<IAzureTableBackup>()), Times.Once());
            azureTableBackupRepository.Verify(x => x.BackupEntitiesAsync(It.IsAny<IAzureTableBackup>(), It.IsAny<List<DynamicTableEntity>>()), Times.Once());
        }
    }
}
