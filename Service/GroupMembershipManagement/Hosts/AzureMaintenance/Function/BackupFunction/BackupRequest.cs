// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Newtonsoft.Json;
using Services.Contracts;
using Services.Entities;

namespace Services.Entities
{
    [JsonObject]
    public class BackupRequest
    {
        public AzureMaintenanceJob BackupMaintenanceJob { get; set; }
    }
}
