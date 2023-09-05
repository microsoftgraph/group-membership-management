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

        public async Task InsertSettingsAsync(Settings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            _context.Settings.Add(settings);
            await _context.SaveChangesAsync();
        }

        public async Task<Settings> GetSettingsAsync(string key)
        {
            return await _context.Settings.FirstOrDefaultAsync(s => s.Key == key);
        }

        public async Task UpdateSettingsAsync(Settings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            _context.Settings.Update(settings);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteSettingsAsync(string key)
        {
            var settings = await _context.Settings.FirstOrDefaultAsync(s => s.Key == key);

            if (settings != null)
            {
                _context.Settings.Remove(settings);
                await _context.SaveChangesAsync();
            }
        }
    }
}