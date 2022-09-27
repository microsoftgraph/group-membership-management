// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;
using Services.Entities;

namespace Services.Contracts
{
    public interface IReviewAndDeleteRequest
    {
        string TableName { get; set; }
        AzureBackup BackupSetting { get; set; }
    }
}
