// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Data.Tables;
using DIConcreteTypes;
using Microsoft.Extensions.Options;
using Models.ThresholdNotifications;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.NotificationsRepository
{
    public class NotificationRepository : INotificationRepository
    {
        private readonly string _thresholdNotificationPartitionKey = "ThresholdNotification";
        private readonly TableClient _tableClient = null;
        private readonly ILoggingRepository _log;

        public NotificationRepository(IOptions<NotificationRepoCredentials<NotificationRepository>> notificationRepoCredentials, ILoggingRepository logger)
        {
            _log = logger ?? throw new ArgumentNullException(nameof(logger));
            _tableClient = new TableClient(notificationRepoCredentials.Value.ConnectionString, notificationRepoCredentials.Value.TableName);
        }

        public async Task<ThresholdNotification> GetThresholdNotificationByIdAsync(Guid notificationId)
        {
            try
            {
                var result = await _tableClient.GetEntityAsync<ThresholdNotificationEntity>(_thresholdNotificationPartitionKey, notificationId.ToString());
                return ToModel(result.Value);
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

        public async Task<ThresholdNotification> GetThresholdNotificationBySyncJobKeysAsync(string syncJobPartitionKey, string syncJobRowKey)
        {
            var queryResult = _tableClient.QueryAsync<ThresholdNotificationEntity>(x =>
                x.SyncJobPartitionKey == syncJobPartitionKey && x.SyncJobRowKey == syncJobRowKey);

            await foreach (var segmentResult in queryResult.AsPages())
            {
                var results = segmentResult.Values.Where(x => x.ResolutionName == ThresholdNotificationResolution.Unresolved.ToString());
                if (results.Count() > 0)
                {
                    return ToModel(results.ElementAt(0));
                }
            }

            return null;
        }

        public async Task SaveNotificationAsync(ThresholdNotification notification)
        {
            var entity = ToEntity(notification);
            await _tableClient.UpsertEntityAsync(entity);
        }

        public async IAsyncEnumerable<ThresholdNotification> GetQueuedNotificationsAsync()
        {
            var notifications = new List<ThresholdNotification>();

            var queryResult = _tableClient.QueryAsync<ThresholdNotificationEntity>(x => x.StatusName == ThresholdNotificationStatus.Queued.ToString());

            await foreach (var segmentResult in queryResult.AsPages())
            {
                var results = segmentResult.Values.Where(x => x.ResolutionName == ThresholdNotificationResolution.Unresolved.ToString());
                foreach (var notification in results)
                {
                    yield return ToModel(notification);
                }
            }
        }

        public async Task UpdateNotificationStatusAsync(ThresholdNotification notification, ThresholdNotificationStatus status)
        {
            var updatedNotification = ToEntity(notification);
            updatedNotification.Status = status;
            await SaveNotificationAsync(ToModel(updatedNotification));
        }

        private ThresholdNotification ToModel(ThresholdNotificationEntity entity)
        {
            return new ThresholdNotification
            {
                Id = entity.Id,
                SyncJobPartitionKey = entity.SyncJobPartitionKey,
                SyncJobRowKey = entity.SyncJobRowKey,
                ChangePercentageForAdditions = entity.ChangePercentageForAdditions,
                ChangePercentageForRemovals = entity.ChangePercentageForRemovals,
                ChangeQuantityForAdditions = entity.ChangeQuantityForAdditions,
                ChangeQuantityForRemovals = entity.ChangeQuantityForRemovals,
                CreatedTime = entity.CreatedTime,
                Resolution = entity.Resolution.GetValueOrDefault(),
                ResolvedByUPN = entity.ResolvedByUPN,
                ResolvedTime = entity.ResolvedTime,
                Status = entity.Status.GetValueOrDefault(),
                TargetOfficeGroupId = entity.TargetOfficeGroupId,
                ThresholdPercentageForAdditions = entity.ThresholdPercentageForAdditions,
                ThresholdPercentageForRemovals = entity.ThresholdPercentageForRemovals
            };
        }

        private ThresholdNotificationEntity ToEntity(ThresholdNotification entity)
        {
            return new ThresholdNotificationEntity
            {
                PartitionKey = _thresholdNotificationPartitionKey,
                RowKey = entity.Id.ToString(),
                Id = entity.Id,
                SyncJobPartitionKey = entity.SyncJobPartitionKey,
                SyncJobRowKey = entity.SyncJobRowKey,
                ChangePercentageForAdditions = entity.ChangePercentageForAdditions,
                ChangePercentageForRemovals = entity.ChangePercentageForRemovals,
                CreatedTime = entity.CreatedTime,
                Resolution = entity.Resolution,
                ResolvedByUPN = entity.ResolvedByUPN,
                ResolvedTime = entity.ResolvedTime,
                Status = entity.Status,
                TargetOfficeGroupId = entity.TargetOfficeGroupId,
                ThresholdPercentageForAdditions = entity.ThresholdPercentageForAdditions,
                ThresholdPercentageForRemovals = entity.ThresholdPercentageForRemovals
            };
        }
    }
}
