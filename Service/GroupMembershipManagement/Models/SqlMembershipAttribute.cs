// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Models
{
    public class SqlMembershipAttribute
    {
        public string Name { get; set; }
        public string CustomLabel { get; set; }
        public string Type { get; set; }
        public bool HasMapping { get; set; }
    }
}
