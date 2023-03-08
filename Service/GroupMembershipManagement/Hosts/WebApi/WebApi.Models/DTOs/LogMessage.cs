// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public class LogMessage
    {
        public Guid? InstanceId { get; set; }
        public string Message { get; set; } = null!;
        public string? MessageTypeName { get; set; }
    }
}