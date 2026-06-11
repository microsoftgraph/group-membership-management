// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace SqlMembershipObtainer
{
    public class SchemaValidatorRequest
    {
        public required string Query { get; init; }
        public required SyncJob SyncJob { get; init; }
        public required int CurrentPart { get; init; }
        public required int TotalParts { get; init; }
    }
}