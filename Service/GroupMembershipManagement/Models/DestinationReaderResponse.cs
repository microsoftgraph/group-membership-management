// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Data.SqlTypes;
using System;

namespace Models
{
    public class DestinationReaderResponse
    {
        public Guid JobId { get; set; }
        public string Destination { get; set; }
    }
}
