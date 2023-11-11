// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Data.SqlTypes;
using Entities;

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
