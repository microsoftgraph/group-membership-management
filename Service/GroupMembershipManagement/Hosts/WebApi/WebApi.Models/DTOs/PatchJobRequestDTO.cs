// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using WebApi.Models.DTOs;

namespace WebApi.Models.DTOs
{
    public class PatchJobRequestDTO
    {
        public List<PatchOperation> PatchOperation { get; set; }
        public string ChangeReason { get; set; }
        public string BusinessJustification { get; set; }
    }

    public class PatchOperation
    {
        public string Op { get; set; }
        public string Path { get; set; }
        public object Value { get; set; }
    }
}
