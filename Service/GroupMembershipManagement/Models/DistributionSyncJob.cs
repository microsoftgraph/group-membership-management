// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Data.SqlTypes;

namespace Models
{
    public class DistributionSyncJob : UpdateMergeSyncJob, IComparable<DistributionSyncJob>
    {
        public Guid TargetOfficeGroupId { get; set; }
        public string Destination { get; set; }
        public DateTime LastRunTime { get; set; } = SqlDateTime.MinValue.Value;
        public int Period { get; set; }
        public string Status { get; set; }

        public DistributionSyncJob(SyncJob syncJob)
        {
            TargetOfficeGroupId = syncJob.TargetOfficeGroupId;
            Destination = syncJob.Destination;
            LastRunTime = syncJob.LastRunTime;
            Period = syncJob.Period;
            Status = syncJob.Status;
            Id = syncJob.Id;
            ScheduledDate = syncJob.ScheduledDate;
        }

        public DistributionSyncJob() { }

        public int CompareTo(DistributionSyncJob other)
        {
            if (Status == other.Status || (Status != SyncStatus.Idle.ToString() && other.Status != SyncStatus.Idle.ToString()))
            {
                return LastRunTime.CompareTo(other.LastRunTime);
            }
            else if (Status == SyncStatus.Idle.ToString())
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
