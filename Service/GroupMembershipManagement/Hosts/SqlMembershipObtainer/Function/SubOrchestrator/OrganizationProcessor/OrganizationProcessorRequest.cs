// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using SqlMembershipObtainer.Entities;

namespace SqlMembershipObtainer
{
    public class OrganizationProcessorRequest
    {
        public Query Query { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}
