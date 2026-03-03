// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Models
{
    public class DestinationInfo
    {
        public string Destination { get; init; } = string.Empty;
        public Guid JobId { get; init; }
    }
}
