// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.JsonPatch;
using WebApi.Models.DTOs;

namespace WebApi.Models
{
    public class PatchJobRequestBody
    {
        public JsonPatchDocument<SyncJobPatch> PatchDocument { get; set; } = default!;
        public string? ChangeReason { get; set; }
        public string? BusinessJustification { get; set; } = string.Empty;
    }
}
