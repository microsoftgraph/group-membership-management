// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IDatabaseSettingsRepository
    {
        Task<Settings> GetSettingsAsync(string key);
        Task InsertSettingsAsync(Settings settings);
        Task UpdateSettingsAsync(Settings settings);
        Task DeleteSettingsAsync(string key);
    }
}
