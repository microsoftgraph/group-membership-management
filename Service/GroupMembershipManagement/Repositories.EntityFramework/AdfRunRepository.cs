// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models.AdfRun;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;
using System;
using System.Threading.Tasks;

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
            return await _readContext.AdfRuns
                .FirstOrDefaultAsync(r => r.AdfRunId == adfRunId);
        }
    }
}
