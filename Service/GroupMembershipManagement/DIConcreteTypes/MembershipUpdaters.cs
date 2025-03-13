// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;

namespace DIConcreteTypes
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
        public string CurrentTopicName { get; set; }
    }
}
