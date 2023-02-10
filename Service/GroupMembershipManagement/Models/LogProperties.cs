// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;

namespace Models
{
    public class LogProperties
    {
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
        public int ConcurrentParts { get; set; }
    }
}
