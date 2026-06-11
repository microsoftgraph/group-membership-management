// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetDefaultSqlMembershipSourceAttributeMappingsRequest : RequestBase
    {
        public GetDefaultSqlMembershipSourceAttributeMappingsRequest(string attribute)
        {
            Attribute = attribute;
        }

        public string Attribute { get; }
    }
}