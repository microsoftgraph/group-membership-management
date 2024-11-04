// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace MessageSplitter.Entities
{
    public class MembershipUpdaters
    {
        public MembershipUpdaters(string currentLaneSize, Dictionary<string, Dictionary<string, Subscription>> availableInstances)
        {
            CurrentLaneSize = currentLaneSize;
            AvailableInstances = availableInstances;
        }

        public string CurrentLaneSize { get; }
        public Dictionary<string, Dictionary<string, Subscription>> AvailableInstances { get; }
    }
}
