// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System;

namespace Services.Entities
{
    public class DistributionSyncJob : UpdateMergeSyncJob, IComparable<DistributionSyncJob>
    {
        public Guid TargetOfficeGroupId { get; set; }
        public DateTime LastRunTime { get; set; } = DateTime.FromFileTimeUtc(0);
        public int Period { get; set; }
        public string Status { get; set; }

        public DistributionSyncJob(SyncJob syncJob) : base(syncJob) {
            TargetOfficeGroupId = syncJob.TargetOfficeGroupId;
            LastRunTime = syncJob.LastRunTime;
            Period = syncJob.Period;
            Status = syncJob.Status;
        }

        public DistributionSyncJob() { }

        public int CompareTo(DistributionSyncJob other)
        {
            if (Status == other.Status || (Status != "Idle" && other.Status != "Idle"))
            {
                return LastRunTime.CompareTo(other.LastRunTime);
            }
            else if (Status == "Idle")
            {
                return -1;
            }
            else
            {
                return 1;
            }
        }
    }
}
