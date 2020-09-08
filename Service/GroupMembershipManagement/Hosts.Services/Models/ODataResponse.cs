// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Newtonsoft.Json;
using System.Diagnostics.CodeAnalysis;

namespace Services.Models
{
    [ExcludeFromCodeCoverage]
    public class ODataResponse<T>
    {
        [JsonProperty(PropertyName = "@odata.context")]
        public string Context { get; set; }

        [JsonProperty(PropertyName = "value")]
        public T Value { get; set; }
    }
}

