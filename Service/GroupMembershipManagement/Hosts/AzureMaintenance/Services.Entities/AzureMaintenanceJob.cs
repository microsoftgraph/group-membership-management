// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities.AzureMaintenance;
using Newtonsoft.Json;
using Services.Entities.Contracts;

namespace Services.Entities
{
    [JsonObject]
    public class AzureMaintenanceJob : IAzureMaintenanceJob
    {
        [JsonProperty]
        public StorageSetting SourceStorageSetting { get; set; }
        [JsonProperty]
        public StorageSetting DestinationStorageSetting { get; set; }
        [JsonProperty]
        public bool Backup { get; set; }
        [JsonProperty]
        public bool Cleanup { get; set; }
        [JsonProperty]
        public int DeleteAfterDays { get; set; }

        public AzureMaintenanceJob(IAzureMaintenanceJob maintenanceJob)
        {
            SourceStorageSetting = maintenanceJob.SourceStorageSetting;
            DestinationStorageSetting = maintenanceJob.DestinationStorageSetting;
            Backup = maintenanceJob.Backup;
            Cleanup = maintenanceJob.Cleanup;
            DeleteAfterDays = maintenanceJob.DeleteAfterDays;
        }

        [JsonConstructor]
        public AzureMaintenanceJob(
            StorageSetting sourceStorageSetting,
            StorageSetting destinationStorageSetting,
            bool backup, bool cleanup, int deleteAfterDays)
        {
            SourceStorageSetting = sourceStorageSetting;
            DestinationStorageSetting = destinationStorageSetting;
            Backup = backup;
            Cleanup = cleanup;
            DeleteAfterDays = deleteAfterDays;
        }

        public override bool Equals(object other)
        {
            if (other != null)
            {
                var otherBackup = other as AzureMaintenanceJob;

                if (otherBackup != null)
                {
                    return SourceStorageSetting.Equals(otherBackup.SourceStorageSetting)
                        && DestinationStorageSetting.Equals(otherBackup.DestinationStorageSetting)
                        && Backup == otherBackup.Backup
                        && Cleanup == otherBackup.Cleanup
                        && DeleteAfterDays == otherBackup.DeleteAfterDays;
                }
            }

            return false;
        }

        public override int GetHashCode()
        {
            return SourceStorageSetting.GetHashCode()
                ^ DestinationStorageSetting.GetHashCode()
                ^ Backup.GetHashCode()
                ^ Cleanup.GetHashCode()
                ^ DeleteAfterDays.GetHashCode();
        }
    }
}
