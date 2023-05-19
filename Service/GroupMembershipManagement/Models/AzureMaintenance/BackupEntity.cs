// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Models.AzureMaintenance
{
    public class BackupEntity
    {
        public string Name { get; set; }
        public string StorageType { get; set; }

        public BackupEntity(string name, string storageType)
        {
            Name = name;
            StorageType = storageType;
        }
    }
}
