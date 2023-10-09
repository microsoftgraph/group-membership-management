// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework
{
    public class DatabaseJobEmailStatusesRepository : IDatabaseJobEmailStatusesRepository
    {
        private readonly GMMContext _context;

        public DatabaseJobEmailStatusesRepository(GMMContext gmmContext)
        {
            _context = gmmContext ?? throw new ArgumentNullException(nameof(gmmContext));
        }

        public async Task<bool> IsEmailDisabledForJob(Guid jobId, int emailTypeId)
        {
            return await _context.JobEmailStatuses
                .AnyAsync(j => j.JobId == jobId && j.EmailTypeId == emailTypeId && j.Status);
        }

    }
}