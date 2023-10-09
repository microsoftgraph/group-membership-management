// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework
{
    public class DatabaseSettingsRepository : IDatabaseSettingsRepository
    {
        private readonly GMMWriteContext _writeContext;
        private readonly GMMReadContext _readContext;

        public DatabaseSettingsRepository(GMMWriteContext writeContext, GMMReadContext readContext)
        {
            _writeContext = writeContext ?? throw new ArgumentNullException(nameof(writeContext));
            _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        }

        public async Task<Setting> GetSettingByKeyAsync(string key)
        {
            return await _readContext.Settings.FirstOrDefaultAsync(s => s.Key == key);
        }

        public async Task<List<Setting>> GetSettingsAsync()
        {
            return await _readContext.Settings.ToListAsync();
        }

        public async Task UpdateSettingAsync(Setting setting, string newSettingValue)
        {
            if (setting == null)
                throw new ArgumentNullException(nameof(setting));

            setting.Value = newSettingValue;
            await _writeContext.SaveChangesAsync();
        }
    }
}