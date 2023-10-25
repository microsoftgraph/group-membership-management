// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace SqlMembershipObtainer
{
    public class ChildEntitiesFilterRequest
    {
        public string Query { get; set; }
        public string TableName { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}
