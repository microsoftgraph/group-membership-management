// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public class SyncJob
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public Guid TargetGroupId { get; set; }
        public string TargetGroupType { get; set; }
        public string Status { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime LastSuccessfulStartTime { get; set; }
        public DateTime LastSuccessfulRunTime { get; set; }
        public DateTime EstimatedNextRunTime { get; set; }
        public int ThresholdPercentageForAdditions { get; set; }
        public int ThresholdPercentageForRemovals { get; set; }
    }
}
