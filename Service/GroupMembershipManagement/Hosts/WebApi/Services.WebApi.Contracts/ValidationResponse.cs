// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Services.WebApi.Contracts
{
    public class ValidationResponse
    {
        public bool IsValid { get; set; }
        public string? ErrorCode { get; set; }
        public List<string>? ResponseData { get; set; }
    }
}
