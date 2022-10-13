// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Hosts.MembershipAggregator
{
    public class DeltaCalculatorRequest
    {
        /// <summary>
        /// Compressed serialized GroupMembership
        /// </summary>
        public string SourceGroupMembership { get; set; }

        /// <summary>
        /// Compressed serialized GroupMembership
        /// </summary>
        public string DestinationGroupMembership { get; set; }
        public bool ReadFromBlobs { get; set; }
        public string SourceMembershipFilePath { get; set; }
        public string DestinationMembershipFilePath { get; set; }
        public Guid? RunId { get; set; }
    }
}

