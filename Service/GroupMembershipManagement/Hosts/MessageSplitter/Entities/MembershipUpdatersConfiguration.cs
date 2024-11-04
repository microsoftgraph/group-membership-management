// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Text.Json.Serialization;

namespace MessageSplitter.Entities
{
    public class MembershipUpdatersConfiguration
    {
        public List<MembershipUpdater> MembershipUpdaters { get; set; }
        public string CurrentLaneSize { get; set; }
    }

    public class MembershipUpdater
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("lanes")]
        public List<Subscription> Lanes { get; set; }
    }

    public class Subscription
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("instances")]
        public int Instances { get; set; }

        [JsonPropertyName("messageSize")]
        public int MessageSize { get; set; }
    }
}
