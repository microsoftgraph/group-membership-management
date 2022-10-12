// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Newtonsoft.Json;
using Services.Contracts;
using System;

namespace Services.Entities
{
    [JsonObject]
    public class ReviewAndDeleteRequest : IReviewAndDeleteRequest
    {
        public string TableName { get; set; }
        public AzureMaintenance MaintenanceSetting { get; set; }
    }
}
