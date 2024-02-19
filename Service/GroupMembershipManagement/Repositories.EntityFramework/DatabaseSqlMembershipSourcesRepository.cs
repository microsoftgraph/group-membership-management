// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework
{
    public class DatabaseSqlMembershipSourcesRepository : IDatabaseSqlMembershipSourcesRepository
    {
        private readonly GMMContext _writeContext;
        private readonly GMMReadContext _readContext;

        public DatabaseSqlMembershipSourcesRepository(GMMContext writeContext, GMMReadContext readContext)
        {
            _writeContext = writeContext ?? throw new ArgumentNullException(nameof(writeContext));
            _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        }

        public async Task<List<SqlMembershipSource>> GetSourcesAsync()
        {
            var sources = await _readContext.SqlMembershipSources
                                            .Select(source => new SqlMembershipSource
                                            {
                                                Name = source.Name,
                                                CustomLabel = source.CustomLabel
                                            })
                                            .ToListAsync();
            return sources;
        }

        public async Task<List<SqlFilterAttribute>> GetSourceAttributesAsync(string sourceName)
        {
            var source = await _readContext.SqlMembershipSources.FirstOrDefaultAsync(s => s.Name == sourceName);
            return source?.Attributes;
        }

        public async Task UpdateSourceAttributesAsync(string sourceName, List<SqlFilterAttribute> attributes)
        {
            var source = await _writeContext.SqlMembershipSources.FirstOrDefaultAsync(s => s.Name == sourceName);
            source.Attributes = attributes;
            await _writeContext.SaveChangesAsync();
        }

        public async Task UpdateSourceCustomLabelAsync(string sourceName, string newCustomLabel)
        {
            var source = await _writeContext.SqlMembershipSources.FirstOrDefaultAsync(s => s.Name == sourceName);
            source.CustomLabel = newCustomLabel;
            await _writeContext.SaveChangesAsync();
        }

        public async Task<SqlMembershipSource> GetDefaultSourceAsync()
        {
            var source = await _writeContext.SqlMembershipSources.FirstOrDefaultAsync(s => s.Name == "SqlMembership");
            return new SqlMembershipSource {
                Name = source.Name,
                CustomLabel = source.CustomLabel
            };
        }

        public async Task<List<SqlFilterAttribute>> GetDefaultSourceAttributesAsync()
        {
            var source = await _writeContext.SqlMembershipSources.FirstOrDefaultAsync(s => s.Name == "SqlMembership");
            return source.Attributes;
        }

        public async Task UpdateDefaultSourceCustomLabelAsync(string newCustomLabel)
        {
            var source = await _writeContext.SqlMembershipSources.FirstOrDefaultAsync(s => s.Name == "SqlMembership");
            source.CustomLabel = newCustomLabel;
            await _writeContext.SaveChangesAsync();
        }

        public async Task UpdateDefaultSourceAttributesAsync(List<SqlFilterAttribute> attributes)
        {
            var source = await _writeContext.SqlMembershipSources.FirstOrDefaultAsync(s => s.Name == "SqlMembership");
            source.Attributes = attributes;
            await _writeContext.SaveChangesAsync();
        }
    }
}