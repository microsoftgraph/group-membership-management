// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;

namespace Hosts.GroupOwnershipObtainer
{
    public class FeatureFlagRequest
    {
        public SyncJob SyncJob { get; set; }
        public int CurrentPart { get; set; }
        public int TotalParts { get; set; }
        public string FeatureFlagName { get; set; }
        public bool RefreshAppConfigurationValues { get; set; }
    }
}