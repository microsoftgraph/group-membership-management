// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Entities.AzureTableBackup
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
