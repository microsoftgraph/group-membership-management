// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.GraphUpdater
{
    public class JobReaderRequest
    {
        public Guid JobId { get; set; }
        public Guid RunId { get; set; }
    }
}
