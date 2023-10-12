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
        private readonly GMMWriteContext _writeContext;
        private readonly GMMReadContext _readContext;

        public DatabaseJobEmailStatusesRepository(GMMWriteContext writeContext, GMMReadContext readContext)
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