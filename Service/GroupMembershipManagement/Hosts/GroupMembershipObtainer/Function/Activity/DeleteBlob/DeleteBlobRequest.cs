// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.GroupMembershipObtainer
{
    public class DeleteBlobRequest
    {
        public Guid RunId { get; set; }
        public Guid GroupId { get; set; }
        public int CurrentPart { get; set; }
    }
}