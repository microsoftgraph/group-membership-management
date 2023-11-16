// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.GroupMembershipObtainer
{
    public class GroupValidatorRequest
    {
        public Guid RunId { get; set; }
        public Guid ObjectId { get; set; }
        public SyncJob SyncJob { get; set; }
        public string Content { get; set; }
        public string[] AdditionalContentParams { get; set; }
    }
}