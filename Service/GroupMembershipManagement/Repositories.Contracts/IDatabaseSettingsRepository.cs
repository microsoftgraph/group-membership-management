// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IDatabaseSettingsRepository
    {
        Task<Setting> GetSettingByKeyAsync(SettingKey settingKey);
        Task<IEnumerable<Setting>> GetAllSettingsAsync();
        Task PatchSettingAsync(SettingKey settingKey, string newSettingValue);
    }
}
