// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Services.Messages.Contracts.Responses;
using System.Net;
using WebApi.Models.DTOs;

namespace Services.Messages.Responses
{
    public class GetDefaultSqlMembershipSourceAttributesResponse : ResponseBase
    {
        public List<SqlMembershipAttribute> Attributes { get; set; }
    }
}