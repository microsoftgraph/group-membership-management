// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Newtonsoft.Json;

namespace SqlMembershipObtainer.Entities
{
    public class Manager
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("depth")]
        public int Depth { get; set; }
    }

    public class Query
    {
        [JsonProperty("manager")]
        public Manager Manager { get; set; }

        [JsonProperty("filter")]
        public string Filter { get; set; }
    }

}