// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using DIConcreteTypes;
using System.Text.Json;

namespace Services.Tests
{
    internal static class Helpers
    {
        public static MembershipUpdaters GetAvailableMembershipUpdaters(string updaters = null, string currentLaneSize = "Small")
        {
            updaters = updaters ?? "[{\"name\":\"GroupMembership\",\"lanes\":[{\"name\":\"small\",\"instances\":1,\"messageSize\":400},{\"name\":\"large\",\"instances\":1,\"messageSize\":400}]}]";
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
