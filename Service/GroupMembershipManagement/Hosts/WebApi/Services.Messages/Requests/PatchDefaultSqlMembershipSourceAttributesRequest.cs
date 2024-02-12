// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class PatchDefaultSqlMembershipSourceAttributesRequest : RequestBase
    {
        public List<SqlMembershipAttribute> Attributes { get; }
        public PatchDefaultSqlMembershipSourceAttributesRequest(List<SqlMembershipAttribute> attributes)
        {
            Attributes = attributes;
        }
    }
}