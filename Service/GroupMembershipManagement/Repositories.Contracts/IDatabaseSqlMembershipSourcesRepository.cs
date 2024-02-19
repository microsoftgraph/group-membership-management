// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IDatabaseSqlMembershipSourcesRepository
    {
        Task<List<SqlMembershipSource>> GetSourcesAsync();
        Task<SqlMembershipSource> GetDefaultSourceAsync();
        Task<List<SqlFilterAttribute>> GetSourceAttributesAsync(string sourceName);
        Task<List<SqlFilterAttribute>> GetDefaultSourceAttributesAsync();
        Task UpdateSourceCustomLabelAsync(string sourceName, string newCustomLabel);
        Task UpdateDefaultSourceCustomLabelAsync(string newCustomLabel);
        Task UpdateSourceAttributesAsync(string sourceName, List<SqlFilterAttribute> attributes);
        Task UpdateDefaultSourceAttributesAsync(List<SqlFilterAttribute> attributes);
    }
}
