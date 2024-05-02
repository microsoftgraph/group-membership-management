// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;

namespace Services.Messages.Responses
{
    public class GetOrgLeaderResponse : ResponseBase
    {
        public string AzureObjectId { get; set; } = string.Empty;
        public int MaxDepth { get; set; }
    }
}