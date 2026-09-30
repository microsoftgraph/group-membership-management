// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;

namespace SqlMembershipObtainer
{
    public class ChildEntitiesFilterRequest
    {
        public required string Query { get; init; }
        public required string TableName { get; init; }
        public required Guid GroupId { get; init; }
        public required SyncJob SyncJob { get; init; }
        public required int CurrentPart { get; init; }
        public required int TotalParts { get; init; }
        public required bool Exclusionary { get; init; }
        public IReadOnlyList<string> IdentityTypes { get; init; }
    }
}
