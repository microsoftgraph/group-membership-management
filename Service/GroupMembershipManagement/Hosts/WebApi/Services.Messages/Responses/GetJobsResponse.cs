// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;

namespace Services.Messages.Responses
{
    public class GetJobsResponse : ResponseBase
    {
        public WebApi.Models.Responses.GetJobsModel Model { get; set; } = new WebApi.Models.Responses.GetJobsModel();
    }
}
