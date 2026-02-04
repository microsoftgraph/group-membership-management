// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Data.SqlTypes;

namespace Models
{
    public class DistributionSyncJob : UpdateMergeSyncJob, IComparable<DistributionSyncJob>
    {
        public DateTime LastRunTime { get; set; } = SqlDateTime.MinValue.Value;
        public int Period { get; set; }
        public string Status { get; set; }
        public int ThresholdPercentageForAdditions { get; set; }
        public int ThresholdPercentageForRemovals { get; set; }


        // NEW PROPERTY: Controls threshold-based prioritization
        public bool PrioritizeThresholdJobs { get; set; } = false;

        public DistributionSyncJob(SyncJob syncJob)
        {
            LastRunTime = syncJob.LastRunTime;
            Period = syncJob.Period;
            Status = syncJob.Status;
            Id = syncJob.Id;
            ScheduledDate = syncJob.ScheduledDate;
            ThresholdPercentageForAdditions = syncJob.ThresholdPercentageForAdditions;
            ThresholdPercentageForRemovals = syncJob.ThresholdPercentageForRemovals;
        }

        public DistributionSyncJob() { }

        public int CompareTo(DistributionSyncJob other)
        {
            // Only apply threshold prioritization when explicitly enabled (e.g., pipeline runs)
            if (PrioritizeThresholdJobs)
            {
                // Sort non-threshold jobs to the end
                bool thisHasThreshold = ThresholdPercentageForAdditions != -1 && ThresholdPercentageForRemovals != -1;
                bool otherHasThreshold = other.ThresholdPercentageForAdditions != -1 && other.ThresholdPercentageForRemovals != -1;

                if (thisHasThreshold && !otherHasThreshold)
                {
                    return -1;
                }
                else if (!thisHasThreshold && otherHasThreshold)
                {
                    return 1;
                }
            }

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
