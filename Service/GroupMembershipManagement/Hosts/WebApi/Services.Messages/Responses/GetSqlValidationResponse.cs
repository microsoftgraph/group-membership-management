// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;

namespace Services.Messages.Responses
{
    public class GetSqlValidationResponse : ResponseBase
    {
        public bool IsValid { get; set; }
        public Dictionary<int, string>? Errors { get; set; }
    }
}
