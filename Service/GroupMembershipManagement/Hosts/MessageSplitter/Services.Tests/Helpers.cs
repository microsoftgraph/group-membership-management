// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using MessageSplitter.Entities;
using System.Text.Json;

namespace Services.Tests
{
    internal static class Helpers
    {
        public static MembershipUpdaters GetAvailableMembershipUpdaters(string updaters = null, string currentLaneSize = "Small")
        {
            updaters = updaters ?? "[{\"name\":\"GroupMembership\",\"lanes\":[{\"name\":\"small\",\"instances\":3,\"messageSize\":20},{\"name\":\"medium\",\"instances\":2,\"messageSize\":60},{\"name\":\"large\",\"instances\":1,\"messageSize\":100},{\"name\":\"onboarding\",\"instances\":1,\"messageSize\":840}]}]";
            var availableMembershipUpdaters = JsonSerializer.Deserialize<List<MembershipUpdater>>(updaters);

            var instances = new Dictionary<string, Dictionary<string, Subscription>>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var updater in availableMembershipUpdaters)
            {
                if (!instances.ContainsKey(updater.Name))
                {
                    instances.Add(updater.Name, new Dictionary<string, Subscription>(StringComparer.InvariantCultureIgnoreCase));

                    foreach (var subscription in updater.Lanes)
                    {
                        if (!instances[updater.Name].ContainsKey(subscription.Name))
                        {
                            instances[updater.Name].Add(subscription.Name, subscription);
                        }
                    }
                }
            }

            return new MembershipUpdaters(currentLaneSize, instances);
        }
    }
}
