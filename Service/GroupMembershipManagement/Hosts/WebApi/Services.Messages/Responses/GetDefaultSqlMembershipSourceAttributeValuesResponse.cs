// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;
using WebApi.Models.Responses;

namespace Services.Messages.Responses
{
    public class GetDefaultSqlMembershipSourceAttributeValuesResponse : ResponseBase
    {
        public GetAttributeValuesModel Model { get; set; } = new GetAttributeValuesModel();
    }
}