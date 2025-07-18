// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;

namespace Hosts.MembershipAggregator
{
    public class LoggerRequest
    {
        public required LogMessage Message { get; init; }
        public required VerbosityLevel Verbosity { get; init; } = VerbosityLevel.INFO;
    }
}
