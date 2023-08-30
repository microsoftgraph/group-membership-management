// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IDatabaseConfigurationRepository
    {
        Task<Configuration> GetConfigurationAsync(Guid id);
        Task InsertConfigurationAsync(Configuration configuration);
        Task UpdateConfigurationAsync(Configuration configuration);
        Task DeleteConfigurationAsync(Configuration configuration);
    }
}
