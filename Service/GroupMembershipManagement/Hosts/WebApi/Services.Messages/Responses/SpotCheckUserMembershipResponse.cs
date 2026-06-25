// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;
using System.Net;
using WebApi.Models.DTOs;

namespace Services.Messages.Responses
{
    public class SpotCheckUserMembershipResponse : ResponseBase
    {
        public SpotCheckResult Model { get; set; }
        public HttpStatusCode StatusCode { get; set; }
    }
}
