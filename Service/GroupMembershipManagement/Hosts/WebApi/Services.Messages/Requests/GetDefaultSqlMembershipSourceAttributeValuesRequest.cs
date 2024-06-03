// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetDefaultSqlMembershipSourceAttributeValuesRequest : RequestBase
    {
        public GetDefaultSqlMembershipSourceAttributeValuesRequest(string attribute)
        {
            Attribute = attribute;
        }

        public string Attribute { get; }
    }
}