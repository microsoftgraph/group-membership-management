// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Hosts.JobTrigger
{
    public class DestinationUpdaterRequest
    {
        public Guid JobId { get; set; }
        public string Destination { get; set; }
    }
}
