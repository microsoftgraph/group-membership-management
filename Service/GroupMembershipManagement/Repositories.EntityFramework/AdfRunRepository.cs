// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models.AdfRun;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework
{
    public class AdfRunRepository : IAdfRunRepository
    {
        private readonly GMMReadContext _readContext;

        public AdfRunRepository(GMMReadContext readContext)
        {
            _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        }

        public async Task<AdfRun?> GetByAdfRunIdAsync(string adfRunId)
        {
            if (string.IsNullOrWhiteSpace(adfRunId))
                return null;

            return await _readContext.AdfRuns
                .FirstOrDefaultAsync(a => a.AdfRunId == adfRunId);
        }

        public async Task<IReadOnlyDictionary<string, string>> GetActiveNotesByAdfRunIdsAsync(IEnumerable<string> adfRunIds)
        {
            var distinctIds = adfRunIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList() ?? new List<string>();

            if (distinctIds.Count == 0)
                return new Dictionary<string, string>();

            var records = await _readContext.AdfRuns
                .Where(a => a.IsActive
                    && a.Notes != null
                    && a.Notes != string.Empty
                    && distinctIds.Contains(a.AdfRunId))
                .Select(a => new { a.AdfRunId, a.Notes })
                .ToListAsync();

            return records.ToDictionary(a => a.AdfRunId, a => a.Notes!);
        }
    }
}
