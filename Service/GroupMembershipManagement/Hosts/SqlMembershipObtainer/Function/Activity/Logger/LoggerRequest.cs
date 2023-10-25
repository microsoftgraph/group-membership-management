// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;

namespace SqlMembershipObtainer
{
    public class LoggerRequest
    {
        public string Message { get; set; }
        public SyncJob SyncJob {  get; set; }
        public VerbosityLevel Verbosity { get; set; } = VerbosityLevel.INFO;
    }
}