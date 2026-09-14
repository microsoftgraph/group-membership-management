// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;
using NewSyncJobDTO = WebApi.Models.DTOs.NewSyncJob;

namespace Services.Messages.Requests
{
    public class PostJobRequest : RequestBase
    {
        public PostJobRequest(string userIdentity,
                              NewSyncJobDTO newSyncJob,
                              bool isJobTenantWriter,
                              string userDisplayName,
                              string businessJustification,
                              bool isOnBehalfAllowed)
        {
            UserIdentity = userIdentity;
            NewSyncJob = newSyncJob;
            IsJobTenantWriter = isJobTenantWriter;
            UserDisplayName = userDisplayName;
            BusinessJustification = businessJustification;
            IsOnBehalfAllowed = isOnBehalfAllowed;
        }
        public NewSyncJobDTO NewSyncJob { get; }
        public string UserIdentity { get; }
        public bool IsJobTenantWriter { get; set; }
        public string UserDisplayName { get; set; }
        public string BusinessJustification { get; set; }

        // True only when the caller is a Job Tenant Writer or Submission Reviewer; gates whether an "on behalf of" owner may be recorded.
        public bool IsOnBehalfAllowed { get; set; }

    }
}