// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;

namespace Hosts.GroupMembershipObtainer
{
    public class FeatureFlagRequest
    {
        public string FeatureFlagName { get; set; }
        public bool RefreshAppConfigurationValues { get; set; }
        public int CurrentPart { get; set; }
        public int TotalParts { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}