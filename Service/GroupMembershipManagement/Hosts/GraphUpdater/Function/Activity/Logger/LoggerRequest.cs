// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;

namespace Hosts.GraphUpdater
{
    public class LoggerRequest
    {
        public string Message { get; set; }
        public SyncJob SyncJob {  get; set; }
    }
}
