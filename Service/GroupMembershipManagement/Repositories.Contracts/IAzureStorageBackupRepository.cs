using Entities.AzureTableBackup;
using Microsoft.Azure.Cosmos.Table;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
	public interface IAzureStorageBackupRepository
	{
        Task<BackupResult> BackupEntitiesAsync(IAzureTableBackup backupSettings, List<DynamicTableEntity> entities);
        Task<List<BackupEntity>> GetBackupsAsync(IAzureTableBackup backupSettings);
		Task<bool> VerifyDeleteBackupAsync(IAzureTableBackup backupSettings, string tableName);
		Task DeleteBackupAsync(IAzureTableBackup backupSettings, string tableName);
	}
}
