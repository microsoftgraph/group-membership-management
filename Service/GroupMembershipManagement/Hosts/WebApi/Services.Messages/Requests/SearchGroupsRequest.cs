// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class SearchGroupsRequest : RequestBase
    {
        public string? Query { get; set; }
    }
}