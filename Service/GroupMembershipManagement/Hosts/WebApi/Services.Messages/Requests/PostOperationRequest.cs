// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class PostOperationRequest : RequestBase
    {
        public PostOperationRequest(Operations operation, Guid requestorId)
        {
            Operation = operation;
            RequestorId = requestorId;
        }

        public Operations Operation { get; }
        public Guid RequestorId { get; }

    }
}
