// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.GroupMembershipObtainer
{
    public class GroupValidatorRequest
    {
        public Guid GroupId { get; set; }
        public Guid ObjectId { get; set; }
        public SyncJob SyncJob { get; set; }
        public string Content { get; set; }
        public string[] AdditionalContentParams { get; set; }
        public int CurrentPart { get; set; }
        public int TotalParts { get; set; }
    }
}