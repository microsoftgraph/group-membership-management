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
            string status,
            DateTime startDate,
            DateTime lastSuccessfulStartTime,
            DateTime lastSuccessfulRunTime,
            DateTime estimatedNextRunTime,
            int thresholdPercentageForAdditions,
            int thresholdPercentageForRemovals)
        {
            PartitionKey = partitionKey;
            RowKey = rowKey;
            TargetGroupId = targetGroupId;
            Status = status;
            StartDate = startDate;
            LastSuccessfulStartTime = lastSuccessfulStartTime;
            LastSuccessfulRunTime = lastSuccessfulRunTime;
            EstimatedNextRunTime = estimatedNextRunTime;
            ThresholdPercentageForAdditions = thresholdPercentageForAdditions;
            ThresholdPercentageForRemovals = thresholdPercentageForRemovals;
        }

        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public Guid TargetGroupId { get; set; }
        public string? TargetGroupType { get; set; }
        public string Status { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime LastSuccessfulStartTime { get; set; }
        public DateTime LastSuccessfulRunTime { get; set; }
        public DateTime EstimatedNextRunTime { get; set; }
        public int ThresholdPercentageForAdditions { get; set; }
        public int ThresholdPercentageForRemovals { get; set; }
    }
}
