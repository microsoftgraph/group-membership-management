// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;
using WebApi.Models.DTOs;

namespace Services.Messages.Responses
{
    public class GetJobDetailsResponse : ResponseBase
    {
        public SyncJobDetails Model { get; set; }
    }
}
