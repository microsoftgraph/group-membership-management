// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using System.Collections.Generic;

namespace Hosts.GraphUpdater
{
    public class LoggerRequest
    {
        public string Message { get; set; }
        public SyncJob SyncJob {  get; set; }
        public VerbosityLevel Verbosity { get; set; } = VerbosityLevel.INFO;
        public Dictionary<string, string> AdditionalProperties { get; set; }
    }
}
