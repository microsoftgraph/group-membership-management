// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using Entities;

namespace Models
{
    public class DistributionSyncJob : UpdateMergeSyncJob, IComparable<DistributionSyncJob>
    {
        public Guid TargetOfficeGroupId { get; set; }
        public DateTime LastRunTime { get; set; } = DateTime.FromFileTimeUtc(0);
        public int Period { get; set; }
        public string Status { get; set; }

        public DistributionSyncJob(SyncJob syncJob)
        {
            TargetOfficeGroupId = syncJob.TargetOfficeGroupId;
            LastRunTime = syncJob.LastRunTime;
            Period = syncJob.Period;
            Status = syncJob.Status;
            PartitionKey = syncJob.PartitionKey;
            RowKey = syncJob.RowKey;
            StartDate = syncJob.StartDate;
        }

        public DistributionSyncJob() { }

        public int CompareTo(Models.DistributionSyncJob other)
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
