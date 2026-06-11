// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Collections.Generic;
using System;

namespace Hosts.DestinationAttributesUpdater
{
    public class AttributeReaderRequest
    {
        public List<DestinationInfo> Destinations { get; set; }
        public string DestinationType { get; set; }
    }
}
