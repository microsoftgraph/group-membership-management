// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework
{
    public class JobNotificationRepository : IJobNotificationsRepository
    {
        private readonly GMMContext _writeContext;
        private readonly GMMReadContext _readContext;

        public JobNotificationRepository(GMMContext writeContext, GMMReadContext readContext)
        {
            _writeContext = writeContext ?? throw new ArgumentNullException(nameof(writeContext));
            _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        }

        public async Task<bool> IsNotificationDisabledForJobAsync(Guid jobId, int notificationTypeId)
        {
            return await _readContext.JobNotifications
                .AnyAsync(j => j.SyncJobId == jobId && j.NotificationTypeID == notificationTypeId && j.Disabled);
        }

    }
}