// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;
using WebApi.Models.Responses;

namespace Services.Messages.Responses
{
    public class GetJobsResponse : ResponseBase
    {
        public GetJobsModel Model { get; set; } = new GetJobsModel();
    }
}
