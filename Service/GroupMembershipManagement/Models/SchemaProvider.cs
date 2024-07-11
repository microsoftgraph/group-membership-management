// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;

namespace Models
{
    public class SchemaProvider
    {
        public Dictionary<Schema, string> Schemas { get; set; } = new Dictionary<Schema, string>();
    }
}