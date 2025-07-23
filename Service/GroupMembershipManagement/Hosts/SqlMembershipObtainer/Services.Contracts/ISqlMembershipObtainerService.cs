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
        Task<GroupMembershipSenderResponse> GetChildEntitiesAsync(string filter, int personnelNumber, string tableName, int depth, SyncJob syncJob, Guid targetOfficeGroupId, int currentPart, bool exclusionary, string adaptiveCardTemplateDirectory);
        Task<GroupMembershipSenderResponse> FilterChildEntitiesAsync(string query, string sourceTableName, SyncJob syncJob, Guid targetOfficeGroupId, int currentPart, bool exclusionary, string adaptiveCardTemplateDirectory);
        Task<GroupMembershipSenderResponse> SendGroupMembershipAsync(List<GraphProfileInformation> profiles, SyncJob syncJob, Guid groupId, int currentPart, bool exclusionary, string adaptiveCardTemplateDirectory = "");
        Task<string> GetTableNameAsync(Guid? runId, Guid? targetOfficeGroupId);
        Task UpdateSyncJobStatusToIdleAsync(SyncJob job);
        Task<Guid> GetGroupIdAsync(SyncJob syncJob);
    }
}
