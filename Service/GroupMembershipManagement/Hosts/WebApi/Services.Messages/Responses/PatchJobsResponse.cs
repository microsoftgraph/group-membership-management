// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;

namespace Services.Messages.Responses
{
    public class PatchJobsResponse : ResponseBase
    {
        public int ApprovedJobsCount { get; set; }
    }
}
