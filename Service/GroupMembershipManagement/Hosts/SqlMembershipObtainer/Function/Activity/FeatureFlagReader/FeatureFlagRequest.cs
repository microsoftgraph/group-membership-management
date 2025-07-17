// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace SqlMembershipObtainer
{
    public class FeatureFlagRequest
    {
        public required Guid RunId { get; init;  }
        public required string FeatureFlagName { get; init; }
        public required bool RefreshAppConfigurationValues { get; init; }
    }
}