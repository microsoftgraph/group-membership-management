// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.MembershipAggregator
{
    public class JobReaderRequest
    {
        public required Guid JobId { get; init; }
        public required Guid RunId { get; init; }
    }
}
