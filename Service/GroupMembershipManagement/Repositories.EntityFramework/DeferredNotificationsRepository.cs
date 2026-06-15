// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Models.Notifications;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework
{
    public class DeferredNotificationsRepository : IDeferredNotificationsRepository
    {
        private readonly GMMContext _writeContext;
        private readonly GMMReadContext _readContext;

        public DeferredNotificationsRepository(GMMContext writeContext, GMMReadContext readContext)
        {
            _writeContext = writeContext ?? throw new ArgumentNullException(nameof(writeContext));
            _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        }

        public async Task AddDeferredNotificationAsync(DeferredNotification deferredNotification)
        {
            _writeContext.DeferredNotifications.Add(deferredNotification);
            try
            {
                await _writeContext.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx
                && (sqlEx.Number == 2601 || sqlEx.Number == 2627))
            {
                // Idempotent: ignore duplicate key violations on SequenceNumber unique index.
                _writeContext.Entry(deferredNotification).State = EntityState.Detached;
            }
        }

        public async Task<IList<DeferredNotification>> GetDeferredNotificationsByTypeAsync(NotificationMessageType messageType)
        {
            return await _readContext.DeferredNotifications
                .Where(d => d.MessageType == messageType)
                .OrderBy(d => d.DeferredAt)
                .ToListAsync();
        }

        public async Task<IList<DeferredNotification>> GetDeferredNotificationsByTypeAndStatusAsync(
            NotificationMessageType messageType, DeferredNotificationStatus status)
        {
            return await _readContext.DeferredNotifications
                .Where(d => d.MessageType == messageType && d.Status == status)
                .OrderBy(d => d.DeferredAt)
                .ToListAsync();
        }

        public async Task UpdateStatusAsync(int id, DeferredNotificationStatus status)
        {
            var entity = await _writeContext.DeferredNotifications.FindAsync(id);
            if (entity != null)
            {
                entity.Status = status;
                if (status == DeferredNotificationStatus.Replayed)
                    entity.ReplayedAt = DateTime.UtcNow;
                await _writeContext.SaveChangesAsync();
            }
        }

        public async Task UpdateStatusBatchAsync(IEnumerable<int> ids, DeferredNotificationStatus status)
        {
            var entities = await _writeContext.DeferredNotifications
                .Where(d => ids.Contains(d.Id))
                .ToListAsync();

            foreach (var entity in entities)
            {
                entity.Status = status;
                if (status == DeferredNotificationStatus.Replayed)
                    entity.ReplayedAt = DateTime.UtcNow;
            }

            await _writeContext.SaveChangesAsync();
        }

        public async Task RemoveDeferredNotificationAsync(int id)
        {
            var entity = await _writeContext.DeferredNotifications.FindAsync(id);
            if (entity != null)
            {
                _writeContext.DeferredNotifications.Remove(entity);
                await _writeContext.SaveChangesAsync();
            }
        }

        public async Task RemoveDeferredNotificationsByTypeAndStatusAsync(
            NotificationMessageType messageType, DeferredNotificationStatus status)
        {
            var entities = await _writeContext.DeferredNotifications
                .Where(d => d.MessageType == messageType && d.Status == status)
                .ToListAsync();

            if (entities.Any())
            {
                _writeContext.DeferredNotifications.RemoveRange(entities);
                await _writeContext.SaveChangesAsync();
            }
        }
    }
}
