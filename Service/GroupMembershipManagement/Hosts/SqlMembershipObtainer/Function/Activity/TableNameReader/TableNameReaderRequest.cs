// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace SqlMembershipObtainer
{
    public class TableNameReaderRequest
    {
        public required SyncJob SyncJob { get; init; }
        public required Guid GroupId { get; init; }
    }
}