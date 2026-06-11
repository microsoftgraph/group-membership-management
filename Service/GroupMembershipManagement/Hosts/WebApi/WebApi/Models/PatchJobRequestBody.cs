// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.JsonPatch;
using System.Text.Json.Serialization;
using WebApi.Models.DTOs;

namespace WebApi.Models
{
    public class PatchJobRequestBody
    {
        [JsonPropertyName("patchDocument")]
        public JsonPatchDocument<SyncJobPatch> PatchDocument { get; set; } = default!;
        
        [JsonPropertyName("changeReason")]
        public string? ChangeReason { get; set; }
        
        [JsonPropertyName("businessJustification")]
        public string? BusinessJustification { get; set; }
    }
}
