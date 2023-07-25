// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.TeamsChannelUpdater
{
    public class JobReaderRequest
    {
        public Guid SyncJobId { get; set; }
        public Guid RunId { get; set; }
    }
}
