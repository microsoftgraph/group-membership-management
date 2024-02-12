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
        Task<List<SqlMembershipAttribute>> GetSourceAttributesAsync(string sourceName);
        Task<List<SqlMembershipAttribute>> GetDefaultSourceAttributesAsync();
        Task UpdateSourceCustomLabelAsync(string sourceName, string newCustomLabel);
        Task UpdateDefaultSourceCustomLabelAsync(string newCustomLabel);
        Task UpdateSourceAttributesAsync(string sourceName, List<SqlMembershipAttribute> attributes);
        Task UpdateDefaultSourceAttributesAsync(List<SqlMembershipAttribute> attributes);
    }
}
