// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Services.TeamsChannelUpdater
{
    public enum Metric
    {
        MembersAdded,
        MembersRemoved,
        MembersAddedFromOnboarding,
        MembersRemovedFromOnboarding,
        GraphAddRatePerSecond,
        GraphRemoveRatePerSecond,
        MembersNotFound,
		ResourceUnitsUsed,
		ThrottleLimitPercentage,
        WritesUsed
	};
}
