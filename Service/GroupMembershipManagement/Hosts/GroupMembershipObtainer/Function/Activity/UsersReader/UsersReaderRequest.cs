// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Hosts.GroupMembershipObtainer
{
    public class UsersReaderRequest
    {
        public Guid RunId { get; set; }
        public Guid ObjectId { get; set; }
    }
}