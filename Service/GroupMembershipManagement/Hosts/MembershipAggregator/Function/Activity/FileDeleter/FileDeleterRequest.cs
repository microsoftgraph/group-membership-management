// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.MembershipAggregator
{
    public class FileDeleterRequest
    {
        public required string FilePath { get; init; }
        public required Guid RunId { get; init; }
    }
}