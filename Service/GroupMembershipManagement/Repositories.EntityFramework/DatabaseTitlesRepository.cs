// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework
{
    public class DatabaseTitlesRepository : IDatabaseTitlesRepository
    {
        private readonly GMMContext _writeContext;
        private readonly GMMReadContext _readContext;

        public DatabaseTitlesRepository(GMMContext writeContext, GMMReadContext readContext)
        {
            _writeContext = writeContext ?? throw new ArgumentNullException(nameof(writeContext));
            _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        }

        public async Task SaveTitlesAsync(Dictionary<string, string> titlesDictionary, Guid syncJobId)
        {
            var titleEntities = titlesDictionary
                .Select(title => new Title
                {
                    SyncJobId = syncJobId,
                    Name = title.Value,
                    PartId = Guid.Parse(title.Key)
                })
                .ToList();

            await _writeContext.AddRangeAsync(titleEntities);
            await _writeContext.SaveChangesAsync();
        }

        public async Task<List<Title>> GetTitlesAsync(Guid syncJobId)
        {
            return await _readContext.Titles
                .Where(title => title.SyncJobId == syncJobId)
                .ToListAsync();
        }

        public async Task UpdateTitlesAsync(List<Title> titles, Guid syncJobId)
        {
            foreach (var title in titles)
            {
                var existingTitle = await _writeContext.Titles
                                                  .FirstOrDefaultAsync(t => t.PartId == title.PartId && t.SyncJobId == syncJobId);

                if (existingTitle != null)
                {
                    existingTitle.Name = title.Name;
                    existingTitle.SyncJobId = title.SyncJobId;
                }
                else
                {
                    _writeContext.Titles.Add(title);
                }
            }
            await _writeContext.SaveChangesAsync();
        }
    }
}