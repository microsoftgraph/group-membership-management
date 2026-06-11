// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace SqlMembershipObtainer
{
    public class FeatureFlagRequest
    {
        public required SyncJob SyncJob { get; init; }
        public required string FeatureFlagName { get; init; }
        public required bool RefreshAppConfigurationValues { get; init; }
        public required int CurrentPart { get; init; }
        public required int TotalParts { get; init; }
    }
}