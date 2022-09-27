// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IAzureBackupService
    {
        Task RunBackupServiceAsync();
        Task<List<IReviewAndDeleteRequest>> RetrieveBackupsAsync();
        Task<bool> ReviewAndDeleteAsync(IAzureBackup backupSetting, string tableName);
    }
}
