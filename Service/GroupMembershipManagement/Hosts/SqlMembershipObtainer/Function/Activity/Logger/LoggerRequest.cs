// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;

namespace SqlMembershipObtainer
{
    public class LoggerRequest
    {
        public required string Message { get; init; }
        public required SyncJob SyncJob { get; init; }
        public required VerbosityLevel Verbosity { get; init; } = VerbosityLevel.INFO;
    }
}