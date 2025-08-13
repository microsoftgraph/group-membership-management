// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Models
{
    public class Title
    {
        public Guid Id { get; set; }

        [ForeignKey("SyncJob")]
        public Guid SyncJobId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("partId")]
        public Guid PartId { get; set; }
    }
}