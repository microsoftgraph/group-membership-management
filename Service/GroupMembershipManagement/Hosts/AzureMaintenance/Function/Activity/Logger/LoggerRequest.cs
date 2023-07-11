// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts;
using System;

namespace Hosts.AzureMaintenance
{
    public class LoggerRequest
    {
        public string Message { get; set; }
        public Guid RunId { get; set; }
        public VerbosityLevel Verbosity { get; set; } = VerbosityLevel.INFO;
    }
}
