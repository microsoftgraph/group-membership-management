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
        private readonly GMMContext _context;

        public DatabaseSettingsRepository(GMMContext gmmContext)
        {
            _context = gmmContext ?? throw new ArgumentNullException(nameof(gmmContext));
        }

        public async Task<Setting> GetSettingByKeyAsync(string key)
        {
            return await _context.Settings.FirstOrDefaultAsync(s => s.Key == key);
        }

        public async Task<List<Setting>> GetSettingsAsync()
        {
            return await _context.Settings.ToListAsync();
        }

        public async Task UpdateSettingAsync(Setting setting, string newSettingValue)
        {
            if (setting == null)
                throw new ArgumentNullException(nameof(setting));

            setting.Value = newSettingValue;
            await _context.SaveChangesAsync();
        }
    }
}