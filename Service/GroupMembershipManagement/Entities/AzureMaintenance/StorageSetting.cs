// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Entities.AzureMaintenance
{
    public class StorageSetting
    {
        public string TargetName { get; set; }
        public string StorageConnectionString { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public StorageType StorageType { get; set; }

        [JsonConstructor]
        public StorageSetting(string targetName, string storageConnectionString, StorageType storageType)
        {
            TargetName = targetName;
            StorageConnectionString = storageConnectionString;
            StorageType = storageType;
        }

        public override bool Equals(object other)
        {
            if (other == null)
            {
                return false;
            }

            var otherBackup = other as StorageSetting;

            return TargetName == otherBackup.TargetName
                && StorageConnectionString == otherBackup.StorageConnectionString
                && StorageType == otherBackup.StorageType;
        }


        public override int GetHashCode()
        {
            return TargetName.GetHashCode()
                ^ StorageConnectionString.GetHashCode()
                ^ StorageType.GetHashCode();
        }
    }
}
