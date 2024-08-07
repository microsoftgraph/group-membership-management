// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;

namespace Services.Messages.Responses
{
    public class GetDefaultSqlMembershipSourceAttributeValuesResponse : ResponseBase
    {
        public List<string> Values { get; set; } = new List<string>();
    }
}