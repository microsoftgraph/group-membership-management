// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Text.Json.Serialization;

namespace SqlMembershipObtainer.Entities
{
    public class Manager
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("depth")]
        public int Depth { get; set; }
    }

    public class Query
    {
        [JsonPropertyName("manager")]
        public Manager Manager { get; set; }

        [JsonPropertyName("filter")]
        public string Filter { get; set; }
    }

}