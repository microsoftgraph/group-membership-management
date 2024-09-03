// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MembershipAggregator;

namespace Services.Tests
{
    public class JobTrackerEntityFake : JobTrackerEntity
    {
        public void SetState(JobState state)
        {
            State = state;
        }
    }
}
