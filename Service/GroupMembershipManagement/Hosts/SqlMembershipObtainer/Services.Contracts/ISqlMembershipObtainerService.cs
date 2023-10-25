// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using SqlMembershipObtainer.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface ISqlMembershipObtainerService
    {
        Task<List<PersonEntity>> GetChildEntitiesAsync(string filter, int personnelNumber, string tableName, int depth, Guid? runId, Guid? targetOfficeGroupId);
        Task<List<PersonEntity>> FilterChildEntitiesAsync(string query, string sourceTableName, Guid? runId, Guid? targetOfficeGroupId);
        Task<(SyncStatus Status, string FilePath)> SendGroupMembershipAsync(List<GraphProfileInformation> profiles, SyncJob syncJob, int currentPart, bool exclusionary, string adaptiveCardTemplateDirectory = "");
        Task<string> GetTableNameAsync(Guid? runId, Guid? targetOfficeGroupId);
        Task UpdateSyncJobStatusToIdleAsync(SyncJob job);
    }
}
