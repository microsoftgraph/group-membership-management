// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework
{
    public class DisabledJobNotificationRepository : IDisabledJobNotificationRepository
    {
        private readonly GMMContext _writeContext;
        private readonly GMMReadContext _readContext;

        public DisabledJobNotificationRepository(GMMContext writeContext, GMMReadContext readContext)
        {
            _writeContext = writeContext ?? throw new ArgumentNullException(nameof(writeContext));
            _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        }

        public async Task<bool> IsNotificationDisabledForJob(Guid jobId, int notificationTypeId)
        {
            return await _readContext.DisabledJobNotifications
                .AnyAsync(j => j.SyncJobId == jobId && j.NotificationTypeID == notificationTypeId && j.Disabled);
        }

    }
}