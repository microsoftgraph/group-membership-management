// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Data.Tables;
using Models.ThresholdNotifications;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Threading.Tasks;

namespace Repositories.NotificationsRepository
{
    public class NotificationRepository : INotificationRepository
    {
        private readonly string _thresholdNotificationPartitionKey = "ThresholdNotification";
        private readonly TableClient _tableClient = null;
        private readonly ILoggingRepository _log;

        public NotificationRepository(INotificationRepoCredentials<NotificationRepository> notificationRepoCredentials, ILoggingRepository logger)
        {
            _log = logger ?? throw new ArgumentNullException(nameof(logger));
            _tableClient = new TableClient(notificationRepoCredentials.TableName, notificationRepoCredentials.ConnectionString);
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
        private ThresholdNotification ToModel(ThresholdNotificationEntity entity)
        {
            return new ThresholdNotification
            {
                Id = entity.Id,
                ChangePercentageForAdditions = entity.ChangePercentageForAdditions,
                CreatedTime = entity.CreatedTime,
                ChangePercentageForRemovals = entity.ChangePercentageForRemovals,
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
                ChangePercentageForAdditions = entity.ChangePercentageForAdditions,
                CreatedTime = entity.CreatedTime,
                ChangePercentageForRemovals = entity.ChangePercentageForRemovals,
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
