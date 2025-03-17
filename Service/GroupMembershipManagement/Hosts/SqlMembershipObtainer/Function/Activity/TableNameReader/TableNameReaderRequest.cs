// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace SqlMembershipObtainer
{
    public class TableNameReaderRequest
    {
        public Guid GroupId { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}