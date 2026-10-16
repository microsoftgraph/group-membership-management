// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;
using System.Data;

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
            if (ConfiguredAttributeApprovalConfiguration.IsConfigurationKey(key))
                throw new ArgumentException("Use the composite configured-attribute configuration operation.", nameof(key));

            var setting = await _writeContext.Settings.FirstOrDefaultAsync(s => s.SettingKey == key);

            if (setting != null)
            {
                setting.SettingValue = newSettingValue;
                _writeContext.Entry(setting).State = EntityState.Modified;
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

        public async Task<ConfiguredAttributeApprovalConfiguration> GetConfiguredAttributeApprovalAsync()
        {
            var settings = await _writeContext.Settings
                .Where(setting => setting.SettingKey == SettingKey.IsAutoApprovalForConfiguredAttributeEnabled
                    || setting.SettingKey == SettingKey.ConfiguredAttributeAutoApprovalConfiguration)
                .AsNoTracking()
                .ToListAsync();
            return ConfiguredAttributeApprovalConfiguration.FromSettings(settings);
        }
        public async Task<ConfiguredAttributeApprovalConfiguration> SaveConfiguredAttributeApprovalAsync(
            ConfiguredAttributeApprovalConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            var document = configuration.SerializeDocument();

            var strategy = _writeContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _writeContext.Database.BeginTransactionAsync();
                await UpsertConfigurationSettingAsync(SettingKey.ConfiguredAttributeAutoApprovalConfiguration, document);
                await UpsertConfigurationSettingAsync(SettingKey.IsAutoApprovalForConfiguredAttributeEnabled, configuration.Enabled ? "true" : "false");
                await _writeContext.SaveChangesAsync();
                await transaction.CommitAsync();
            });
            return configuration;
        }
        private async Task UpsertConfigurationSettingAsync(SettingKey key, string value)
        {
            var setting = await _writeContext.Settings.FirstOrDefaultAsync(s => s.SettingKey == key);

            if (setting != null)
            {
                setting.SettingValue = value;
                _writeContext.Entry(setting).State = EntityState.Modified;
            }
            else
            {
                await _writeContext.Settings.AddAsync(new Setting
                {
                    Id = SettingConstants.SettingIds[key],
                    SettingKey = key,
                    SettingValue = value
                });
            }
        }
    }
}