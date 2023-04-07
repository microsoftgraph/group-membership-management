// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Models
{
    public class MembershipAggregatorHttpRequest : MembershipHttpRequest
    {
        public int PartNumber { get; set; }
        public int PartsCount { get; set; }
        public bool IsDestinationPart { get; set; }
    }
}
