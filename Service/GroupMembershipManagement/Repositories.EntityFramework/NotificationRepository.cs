// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Models.ThresholdNotifications;
using Repositories.Contracts;
using Microsoft.EntityFrameworkCore;
using ModelNotification = Models.ThresholdNotifications.ThresholdNotification;
using EntityNotification = Entities.ThresholdNotification;
using Repositories.EntityFramework.Contexts;

namespace Repositories.NotificationsRepository
{
    public class NotificationRepository : INotificationRepository
    {
        private readonly GMMContext _writeContext;
        private readonly GMMReadContext _readContext;

        public NotificationRepository(GMMContext writeContext, GMMReadContext readContext)
        {
            _writeContext = writeContext ?? throw new ArgumentNullException(nameof(writeContext));
            _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        }

        public async Task<ModelNotification?> GetThresholdNotificationByIdAsync(Guid notificationId)
        {
            try
            {
                var result = await _readContext.ThresholdNotifications.FirstOrDefaultAsync(n => n.Id == notificationId);
                return result != null ? ToModel(result) : null;
            }
            catch (RequestFailedException ex)
            {
                if (ex.Status != 404) // record not found
                {
                    throw ex;
                }
            }
            return null;
        }

        public async Task<ModelNotification?> GetThresholdNotificationBySyncJobIdAsync(Guid syncJobId)
        {
            var resolutionNameString = ThresholdNotificationResolution.Unresolved.ToString();

            var queryResult = await _readContext.ThresholdNotifications
                                .Where(n => n.SyncJobId == syncJobId &&
                                            n.ResolutionName == resolutionNameString)
                                .FirstOrDefaultAsync();

            if (queryResult != null)
            {
                return ToModel(queryResult);
            }

            return null;
        }

        public async Task SaveNotificationAsync(ModelNotification notification)
        {
            var entityNotification = ToEntity(notification);

            var existingNotification = await _readContext.ThresholdNotifications
                .FirstOrDefaultAsync(n => n.Id == entityNotification.Id);

            if (existingNotification == null)
            {
                _writeContext.ThresholdNotifications.Add(entityNotification);
            }
            else
            {
                _writeContext.Entry(existingNotification).CurrentValues.SetValues(entityNotification);
            }
            await _writeContext.SaveChangesAsync();
        }

        public async IAsyncEnumerable<ModelNotification> GetQueuedNotificationsAsync()
        {
            var notifications = new List<ModelNotification>();

            {
                var query = _readContext.ThresholdNotifications
                                    .Where(n => n.StatusName == ThresholdNotificationStatus.Queued.ToString() &&
                                                n.ResolutionName == ThresholdNotificationResolution.Unresolved.ToString());

                await foreach (var notification in query.AsAsyncEnumerable())
                {
                    yield return ToModel(notification);
                }
            }
        }

        public async Task UpdateNotificationStatusAsync(ModelNotification notification, ThresholdNotificationStatus status)
        {
            var updatedNotification = ToEntity(notification);
            updatedNotification.Status = status;
            await SaveNotificationAsync(ToModel(updatedNotification));
        }

        private ModelNotification ToModel(EntityNotification entity)
        {
            return new ModelNotification
            {
                Id = entity.Id,
                SyncJobId = entity.SyncJobId,
                ChangePercentageForAdditions = entity.ChangePercentageForAdditions,
                ChangePercentageForRemovals = entity.ChangePercentageForRemovals,
                ChangeQuantityForAdditions = entity.ChangeQuantityForAdditions,
                ChangeQuantityForRemovals = entity.ChangeQuantityForRemovals,
                CreatedTime = entity.CreatedTime,
                Resolution = entity.Resolution.GetValueOrDefault(),
                ResolvedBy = entity.ResolvedBy,
                ResolvedTime = entity.ResolvedTime,
                Status = entity.Status.GetValueOrDefault(),
                CardState = entity.CardState.GetValueOrDefault(),
                TargetOfficeGroupId = entity.TargetOfficeGroupId,
                ThresholdPercentageForAdditions = entity.ThresholdPercentageForAdditions,
                ThresholdPercentageForRemovals = entity.ThresholdPercentageForRemovals,
                LastUpdatedTime = entity.LastUpdatedTime
            };
        }

        private EntityNotification ToEntity(ModelNotification entity)
        {
            return new EntityNotification
            {
                Id = entity.Id,
                SyncJobId = entity.SyncJobId,
                ChangePercentageForAdditions = entity.ChangePercentageForAdditions,
                ChangePercentageForRemovals = entity.ChangePercentageForRemovals,
                ChangeQuantityForAdditions = entity.ChangeQuantityForAdditions,
                ChangeQuantityForRemovals = entity.ChangeQuantityForRemovals,
                CreatedTime = entity.CreatedTime,
                Resolution = entity.Resolution,
                ResolvedBy = entity.ResolvedBy,
                ResolvedTime = entity.ResolvedTime,
                Status = entity.Status,
                CardState = entity.CardState,
                TargetOfficeGroupId = entity.TargetOfficeGroupId,
                ThresholdPercentageForAdditions = entity.ThresholdPercentageForAdditions,
                ThresholdPercentageForRemovals = entity.ThresholdPercentageForRemovals
            };
        }
    }
}
