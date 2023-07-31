// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Hosts.GroupMembershipObtainer
{
    public class GetUserCountRequest
    {
        public Guid RunId { get; set; }
        public Guid GroupId { get; set; }
    }
}