// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;
using WebApi.Models.Responses;

namespace Services.Messages.Responses
{
    public class GetGroupResponse : ResponseBase
    {
        public GetGroupsModel Model { get; set; } = new GetGroupsModel();
    }
}
