// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;

namespace Models
{
    public class DestinationAttributes
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public List<Guid> Owners { get; set; }
    }
}
