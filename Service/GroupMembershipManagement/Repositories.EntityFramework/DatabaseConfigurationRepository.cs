// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;
using System;

namespace Repositories.EntityFramework
{
    public class DatabaseConfigurationRepository : IDatabaseConfigurationRepository
    {
        private readonly GMMContext _context;

        public DatabaseConfigurationRepository(GMMContext gmmContext)
        {
            _context = gmmContext ?? throw new ArgumentNullException(nameof(gmmContext));
        }

        public async Task InsertConfigurationAsync(Configuration configuration)
        {
            var entry = _context.Set<Configuration>().Add(configuration);
            entry.State = EntityState.Added;

            await _context.SaveChangesAsync();
        }

        public async Task<Configuration> GetConfigurationAsync(Guid id)
        {
            return await _context.Configuration.SingleOrDefaultAsync(configuration => configuration.Id == id);
        }

        public async Task UpdateConfigurationAsync(Configuration configuration)
        {
            
            var entry = _context.Set<Configuration>().Add(configuration);
            entry.State = EntityState.Modified;

            await _context.SaveChangesAsync();
        }

        public async Task DeleteConfigurationAsync(Configuration configuration)
        {
            var entry = _context.Set<Configuration>().Add(configuration);
            entry.State = EntityState.Deleted;

            await _context.SaveChangesAsync();
        }
    }
}