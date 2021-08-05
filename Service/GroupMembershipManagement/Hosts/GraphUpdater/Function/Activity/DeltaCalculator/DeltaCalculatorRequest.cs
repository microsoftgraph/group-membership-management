// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using System;
using System.Collections.Generic;

namespace Hosts.GraphUpdater
{
    public class DeltaCalculatorRequest
    {
        public Guid RunId { get; set; }
        public GroupMembership GroupMembership { get; set; }
        public List<AzureADUser> MembersFromDestinationGroup { get; set; }
    }
}
