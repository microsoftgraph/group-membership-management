using Entities.AzureMaintenance;
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
        Task<BackupResult> BackupEntitiesAsync(IAzureMaintenance backupSettings, List<DynamicTableEntity> entities);
        Task<List<BackupEntity>> GetBackupsAsync(IAzureMaintenance backupSettings);
		Task<bool> VerifyDeleteBackupAsync(IAzureMaintenance backupSettings, string tableName);
		Task DeleteBackupAsync(IAzureMaintenance backupSettings, string tableName);
	}
}
