// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.TeamsChannelUpdater
{
    public class GroupNameReaderRequest
    {
        public Guid GroupId { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}