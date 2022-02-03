// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities.ServiceBus;

namespace Entities
{
    public class GraphUpdaterFunctionRequest
    {
        public GroupMembership Membership { get; set; }
        public string MessageSessionId { get; set; }
        public bool IsCancelationRequest { get; set; }
    }
}
