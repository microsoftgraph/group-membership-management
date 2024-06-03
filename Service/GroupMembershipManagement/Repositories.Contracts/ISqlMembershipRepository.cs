// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using SqlMembershipObtainer.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface ISqlMembershipRepository
    {
        Task<List<PersonEntity>> GetChildEntitiesAsync(string filter, int personnelNumber, string tableName, int depth);
        Task<(int maxDepth, int id)> GetOrgLeaderDetailsAsync(string azureObjectId, string tableName);
        Task<List<PersonEntity>> FilterChildEntitiesAsync(string query, string tableName);
        Task<bool> CheckIfTableExistsAsync(string tableName);
        Task<List<string>> GetColumnNamesAsync(string tableName);
        Task<(int maxDepth, string azureObjectId)> GetOrgLeaderAsync(int employeeId, string tableName);
        Task<List<(string Name, string Type)>> GetColumnDetailsAsync(string tableName);
        Task<bool> CheckIfMappingsTableExistsAsync(string tableName);
        Task<List<(string Code, string Description)>> GetAttributeValuesAsync(string attribute, string tableName);
    }
}
