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
            var existingTitles = await _writeContext.Titles
                .Where(t => t.SyncJobId == syncJobId)
                .ToListAsync();

            var existingTitlesDict = existingTitles.ToDictionary(t => t.PartId);
            var validIncomingTitles = titles.Where(t => !string.IsNullOrEmpty(t.Name)).ToList();
            var incomingPartIds = validIncomingTitles.Select(t => t.PartId).ToHashSet();

            var titlesToAdd = new List<Title>();
            foreach (var title in validIncomingTitles)
            {
                if (existingTitlesDict.TryGetValue(title.PartId, out var existingTitle))
                {
                    existingTitle.Name = title.Name;
                }
                else
                {
                    titlesToAdd.Add(title);
                }
            }

            var titlesToDelete = existingTitles
                .Where(t => !incomingPartIds.Contains(t.PartId))
                .ToList();

            if (titlesToAdd.Any())
            {
                await _writeContext.Titles.AddRangeAsync(titlesToAdd);
            }

            if (titlesToDelete.Any())
            {
                _writeContext.Titles.RemoveRange(titlesToDelete);
            }

            await _writeContext.SaveChangesAsync();
        }

        public async Task DeleteTitlesAsync(Guid syncJobId)
        {
            await _writeContext.Titles
                .Where(t => t.SyncJobId == syncJobId)
                .ExecuteDeleteAsync();
        }
    }
}