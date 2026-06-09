// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.TeamsChannelUpdater
{
    public class JobReaderRequest
    {
        public Guid JobId { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}
