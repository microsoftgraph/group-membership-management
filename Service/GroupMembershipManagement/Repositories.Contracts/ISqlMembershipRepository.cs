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
        Task<List<PersonEntity>> FilterChildEntitiesAsync(string query, string tableName);
        Task<bool> CheckIfTableExistsAsync(string tableName);
        Task<List<string>> GetColumnNamesAsync(string tableName);
    }
}
