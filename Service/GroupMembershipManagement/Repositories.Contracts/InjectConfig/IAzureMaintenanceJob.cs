// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.AzureMaintenance;

namespace Services.Entities.Contracts
{
    public interface IAzureMaintenanceJob
    {
        public StorageSetting SourceStorageSetting { get; set; }
        public StorageSetting DestinationStorageSetting { get; set; }
        public bool Backup { get; set;  }
        public bool Cleanup { get; set; }
        public int DeleteAfterDays { get; set; }
    }
}
