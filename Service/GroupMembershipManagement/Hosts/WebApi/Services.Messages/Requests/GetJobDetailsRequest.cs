// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetJobDetailsRequest : RequestBase
    {
        public GetJobDetailsRequest(Guid syncJobId)
        {
            this.SyncJobId = syncJobId;
        }

        public Guid SyncJobId { get; }
    }
}