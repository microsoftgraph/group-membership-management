// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IDatabaseSettingsRepository
    {
        Task<Setting> GetSettingByKeyAsync(string key);
        Task<List<Setting>> GetSettingsAsync();
        Task UpdateSettingAsync(Setting setting, string newSettingValue);
    }
}
