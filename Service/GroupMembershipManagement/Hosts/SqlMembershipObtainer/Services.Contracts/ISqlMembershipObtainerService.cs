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
        Task<MembershipFileResult> GetChildEntitiesAsync(string filter, int personnelNumber, string tableName, int depth, SyncJob syncJob, Guid targetOfficeGroupId, int currentPart, bool exclusionary);
        Task<MembershipFileResult> FilterChildEntitiesAsync(string query, string sourceTableName, SyncJob syncJob, Guid targetOfficeGroupId, int currentPart, bool exclusionary);
        Task<MembershipFileResult> UploadMembershipFileAsync(List<GraphProfileInformation> profiles, SyncJob syncJob, Guid groupId, int currentPart, bool exclusionary);
        Task<string> GetTableNameAsync(Guid? runId, Guid? targetOfficeGroupId);
        Task UpdateSyncJobStatusToIdleAsync(SyncJob job);
        Task<Guid> GetGroupIdAsync(SyncJob syncJob);
    }
}
