using Entities.AzureBackup;
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
        Task<BackupResult> BackupEntitiesAsync(IAzureBackup backupSettings, List<DynamicTableEntity> entities);
        Task<List<BackupEntity>> GetBackupsAsync(IAzureBackup backupSettings);
		Task<bool> VerifyDeleteBackupAsync(IAzureBackup backupSettings, string tableName);
		Task DeleteBackupAsync(IAzureBackup backupSettings, string tableName);
	}
}
