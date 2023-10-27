// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework
{
    public class JobEmailStatusesRepository : IJobEmailStatusesRepository
    {
        private readonly GMMContext _writeContext;
        private readonly GMMReadContext _readContext;

        public JobEmailStatusesRepository(GMMContext writeContext, GMMReadContext readContext)
        {
            _writeContext = writeContext ?? throw new ArgumentNullException(nameof(writeContext));
            _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        }

        public async Task<bool> IsEmailDisabledForJob(Guid jobId, int emailTypeId)
        {
            return await _readContext.JobEmailStatuses
                .AnyAsync(j => j.SyncJobId == jobId && j.EmailTypeId == emailTypeId && j.DisableEmail);
        }

    }
}