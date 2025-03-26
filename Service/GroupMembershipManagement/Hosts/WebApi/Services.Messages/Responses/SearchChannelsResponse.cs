// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;
using WebApi.Models.Responses;

namespace Services.Messages.Responses
{
    public class SearchChannelsResponse : ResponseBase
    {
        public GetChannelsModel Model { get; set; } = new GetChannelsModel();
    }
}
