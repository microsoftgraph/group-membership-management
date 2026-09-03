// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using MembershipAggregator.Services.Entities;

namespace Hosts.MembershipAggregator
{
    public class DeltaCalculatorResponse
    {
        public int MembersToAddCount { get; set; }
        public int MembersToRemoveCount { get; set; }
        public int SourceMemberCount { get; set; }
        public int DestinationMemberCount { get; set; }
        public MembershipDeltaStatus MembershipDeltaStatus { get; set; }
        public string MembersToAddFilePath { get; set; }
        public string MembersToRemoveFilePath { get; set; }
        public string DeltaManifestFilePath { get; set; }
        public bool? UseStagedDeltaFiles { get; set; }

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
