// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Newtonsoft.Json;
using System.Collections.Generic;

namespace SqlMembershipObtainer.Entities
{
    public class Query
    {
        [JsonProperty("ids")]
        public List<int> Ids { get; set; }

        [JsonProperty("depth")]
        public int Depth { get; set; }

        [JsonProperty("filter")]
        public string Filter { get; set; }
    }
}
