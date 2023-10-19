// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;

namespace Services.Messages.Responses
{
    public class GetGroupEndpointsResponse : ResponseBase
    {
        public List<string> Endpoints {  get; set; } = new List<string>();
    }
}
