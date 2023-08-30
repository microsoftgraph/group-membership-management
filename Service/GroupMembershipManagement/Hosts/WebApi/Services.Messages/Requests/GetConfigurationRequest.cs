// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetConfigurationRequest : RequestBase
    {
        public GetConfigurationRequest(Guid id)
        {
            this.Id = id;
        }

        public Guid Id { get; }
    }
}