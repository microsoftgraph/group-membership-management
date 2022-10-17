// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;
using Services.Entities.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IAzureMaintenanceService
    {
        Task RunBackupServiceAsync(IAzureMaintenanceJob maintenanceJob);
        Task<List<IReviewAndDeleteRequest>> RetrieveBackupsAsync(IAzureMaintenanceJob maintenanceJob);
        Task<bool> ReviewAndDeleteAsync(IAzureMaintenanceJob maintenanceJob, string tableName);
    }
}
