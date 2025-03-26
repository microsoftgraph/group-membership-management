// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class SearchChannelsRequest : RequestBase
    {
        public Guid TeamId { get; set; }
        public string? Query { get; set; }
    }
}