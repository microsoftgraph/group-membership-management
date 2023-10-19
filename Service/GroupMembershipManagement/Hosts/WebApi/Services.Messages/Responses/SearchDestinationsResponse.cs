// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;
using WebApi.Models.Responses;

namespace Services.Messages.Responses
{
    public class SearchDestinationsResponse : ResponseBase
    {
        public GetDestinationsModel Model { get; set; } = new GetDestinationsModel();
    }
}
