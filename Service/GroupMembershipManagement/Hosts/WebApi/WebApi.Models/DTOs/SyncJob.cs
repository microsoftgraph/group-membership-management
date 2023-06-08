// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public class SyncJob
    {
        public SyncJob(
            string partitionKey,
            string rowKey,
            Guid targetGroupId,
            string? targetGroupName,
            string status,
            DateTime lastSuccessfulRunTime,
            DateTime estimatedNextRunTime)
        {
            PartitionKey = partitionKey;
            RowKey = rowKey;
            TargetGroupId = targetGroupId;
            TargetGroupName = targetGroupName;
            Status = status;
            LastSuccessfulRunTime = lastSuccessfulRunTime;
            EstimatedNextRunTime = estimatedNextRunTime;
        }

        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public Guid TargetGroupId { get; set; }
        public string? TargetGroupName { get; set; }
        public string? TargetGroupType { get; set; }
        public string Status { get; set; }
        public DateTime LastSuccessfulRunTime { get; set; }
        public DateTime EstimatedNextRunTime { get; set; }
    }
}
