// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public class SyncJob
    {
        public SyncJob(
            Guid id,
            Guid targetGroupId,
            string status,
            int period,
            DateTime lastSuccessfulRunTime,
            DateTime estimatedNextRunTime)
        {
            SyncJobId = id;
            TargetGroupId = targetGroupId;
            Status = status;
            Period = period;
            LastSuccessfulRunTime = lastSuccessfulRunTime;
            EstimatedNextRunTime = estimatedNextRunTime;
        }

        public Guid SyncJobId { get; set; }
        public Guid TargetGroupId { get; set; }
        public string? TargetGroupName { get; set; }
        public string? TargetGroupType { get; set; }
        public string Status { get; set; }
        public int Period { get; set; }

        public DateTime LastSuccessfulRunTime { get; set; }
        public DateTime EstimatedNextRunTime { get; set; }
    }
}
