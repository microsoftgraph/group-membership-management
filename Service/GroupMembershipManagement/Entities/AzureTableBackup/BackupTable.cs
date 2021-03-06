// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Entities.AzureTableBackup
{
    public class BackupTable
    {
        public string TableName { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
