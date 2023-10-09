// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework
{
    public class DatabaseEmailStatusesRepository : IDatabaseEmailStatusesRepository
    {
        private readonly GMMContext _context;

        public DatabaseEmailStatusesRepository(GMMContext gmmContext)
        {
            _context = gmmContext ?? throw new ArgumentNullException(nameof(gmmContext));
        }

        public async Task<EmailStatus> GetOnBoardingEmailStatusAsync(Guid syncJobId, int emailTypeId)
        {
            return await _context.EmailStatuses.SingleOrDefaultAsync(job => job.SyncJobId == syncJobId && job.EmailTypeId == emailTypeId);
        }

    }
}