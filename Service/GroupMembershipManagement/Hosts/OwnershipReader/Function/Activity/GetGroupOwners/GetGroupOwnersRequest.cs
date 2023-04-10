// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;

namespace Hosts.OwnershipReader
{
    public class GetGroupOwnersRequest
    {
        public Guid GroupId { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}
