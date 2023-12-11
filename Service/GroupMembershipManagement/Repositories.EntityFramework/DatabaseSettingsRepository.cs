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
        private readonly GMMContext _writeContext;
        private readonly GMMReadContext _readContext;

        public DatabaseSettingsRepository(GMMContext writeContext, GMMReadContext readContext)
        {
            _writeContext = writeContext ?? throw new ArgumentNullException(nameof(writeContext));
            _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        }

        public async Task<IEnumerable<Setting>> GetAllSettingsAsync()
        {
            return await _readContext.Settings.ToListAsync();
        }
        public async Task<Setting> GetSettingByKeyAsync(SettingKey key)
        {
            return await _readContext.Settings.FirstOrDefaultAsync(s => s.SettingKey == key);
        }

        public async Task PatchSettingAsync(SettingKey key, string newSettingValue)
        {
            var setting = await _writeContext.Settings.FirstOrDefaultAsync(s => s.SettingKey == key);

            if (setting != null)
            {
                setting.SettingValue = newSettingValue;
            }
            else
            {
                var newSetting = new Setting
                {
                    Id = SettingConstants.SettingIds[key],
                    SettingKey = key,
                    SettingValue = newSettingValue
                };

                await _writeContext.Settings.AddAsync(newSetting);
            }

            await _writeContext.SaveChangesAsync();
        }
    }
}