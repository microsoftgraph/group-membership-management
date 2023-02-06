// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Models.Entities
{
    public class LogProperties
    {
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
        public int ConcurrentParts { get; set; }
    }
}
