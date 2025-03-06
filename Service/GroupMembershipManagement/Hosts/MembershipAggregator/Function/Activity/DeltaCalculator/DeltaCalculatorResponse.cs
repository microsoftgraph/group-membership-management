// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Services.Entities;

namespace Hosts.MembershipAggregator
{
    public class DeltaCalculatorResponse
    {
        public int MembersToAddCount { get; set; }
        public int MembersToRemoveCount { get; set; }
        public MembershipDeltaStatus MembershipDeltaStatus { get; set; }

        /// <summary>
        /// Contains a compressed serialized ICollection<AzureADUser>
        /// </summary>
        public string CompressedMembersToAddJSON { get; set; }

        /// <summary>
        /// Contains a compressed serialized ICollection<AzureADUser>
        /// </summary>
        public string CompressedMembersToRemoveJSON { get; set; }
    }
}

