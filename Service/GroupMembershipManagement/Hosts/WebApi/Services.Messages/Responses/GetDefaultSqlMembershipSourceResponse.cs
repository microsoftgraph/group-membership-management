// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Services.Messages.Contracts.Responses;

namespace Services.Messages.Responses
{
    public class GetDefaultSqlMembershipSourceResponse : ResponseBase
    {
        public SqlMembershipSource Model { get; set; }
    }
}
