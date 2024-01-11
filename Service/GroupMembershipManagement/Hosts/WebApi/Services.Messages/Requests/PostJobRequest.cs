// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;
using NewSyncJobDTO = WebApi.Models.DTOs.NewSyncJob;

namespace Services.Messages.Requests
{
    public class PostJobRequest : RequestBase
    {
        public PostJobRequest(string userIdentity, NewSyncJobDTO newSyncJob)
        {
            UserIdentity = userIdentity;
            NewSyncJob = newSyncJob;
        }
        public NewSyncJobDTO NewSyncJob { get; }
        public string UserIdentity { get; }

    }
}