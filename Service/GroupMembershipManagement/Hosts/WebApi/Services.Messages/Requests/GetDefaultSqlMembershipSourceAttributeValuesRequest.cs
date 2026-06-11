// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetDefaultSqlMembershipSourceAttributeValuesRequest : RequestBase
    {
        public GetDefaultSqlMembershipSourceAttributeValuesRequest(string attribute, bool hasMapping)
        {
            Attribute = attribute;
            HasMapping = hasMapping;
        }

        public string Attribute { get; }
        public bool HasMapping { get; }
    }
}