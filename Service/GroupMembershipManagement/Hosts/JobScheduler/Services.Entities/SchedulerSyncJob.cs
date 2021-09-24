// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System;
using System.Diagnostics.CodeAnalysis;
namespace Services.Entities
{
    public class SchedulerSyncJob : SyncJob, IComparable<SchedulerSyncJob>
    {
        public SchedulerSyncJob() { }

        public int CompareTo(SchedulerSyncJob other)
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
