// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace SqlMembershipObtainer
{
    public class GetGroupRequest
    {
        public required SyncJob SyncJob { get; init; }
        public required int CurrentPart { get; init; }
        public required int TotalParts { get; init; }
    }
}
