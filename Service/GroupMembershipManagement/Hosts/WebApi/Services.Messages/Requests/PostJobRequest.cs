// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;
using NewSyncJobDTO = WebApi.Models.DTOs.NewSyncJob;

namespace Services.Messages.Requests
{
    public class PostJobRequest : RequestBase
    {
        public PostJobRequest(string userIdentity, NewSyncJobDTO newSyncJob, bool isJobTenantWriter)
        {
            UserIdentity = userIdentity;
            NewSyncJob = newSyncJob;
            IsJobTenantWriter = isJobTenantWriter;
        }
        public NewSyncJobDTO NewSyncJob { get; }
        public string UserIdentity { get; }
        public bool IsJobTenantWriter { get; set; }

    }
}