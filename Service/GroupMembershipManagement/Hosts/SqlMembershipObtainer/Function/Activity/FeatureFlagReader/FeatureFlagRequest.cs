// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace SqlMembershipObtainer
{
    public class FeatureFlagRequest
    {
        public Guid? RunId { get; set; }
        public string FeatureFlagName { get; set; }
        public bool RefreshAppConfigurationValues { get; set; }
    }
}