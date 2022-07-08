// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts;

namespace Hosts.MembershipAggregator
{
    public class LoggerRequest
    {
        public LogMessage Message { get; set; }
        public VerbosityLevel Verbosity { get; set; } = VerbosityLevel.INFO;
    }
}
