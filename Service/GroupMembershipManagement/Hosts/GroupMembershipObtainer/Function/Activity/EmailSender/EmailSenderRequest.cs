// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.GroupMembershipObtainer
{
    public class EmailSenderRequest
    {
        public Guid RunId { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}