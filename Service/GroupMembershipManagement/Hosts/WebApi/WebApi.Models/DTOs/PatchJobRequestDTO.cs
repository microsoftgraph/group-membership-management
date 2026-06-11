// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Text.Json;
using WebApi.Models.DTOs;

namespace WebApi.Models.DTOs
{
    public class PatchJobRequestDTO
    {
        public List<PatchOperation> PatchOperation { get; set; } = new List<PatchOperation>();
        public string ChangeReason { get; set; }
        public string BusinessJustification { get; set; }
    }

    public class PatchOperation
    {
        public string? Op { get; set; }
        public string? Path { get; set; }
        public JsonElement? Value { get; set; }
    }
}
