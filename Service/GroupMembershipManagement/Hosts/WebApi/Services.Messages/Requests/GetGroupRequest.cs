// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetGroupRequest : RequestBase
    {
        public string? Query { get; set; }
    }
}