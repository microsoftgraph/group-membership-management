// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;
using NewSyncJobDTO = WebApi.Models.DTOs.NewSyncJob;

namespace Services.Messages.Requests
{
    public class PostJobRequest : RequestBase
    {
        public PostJobRequest(bool isAdmin, string userIdentity, NewSyncJobDTO newSyncJob)
        {
            IsAdmin = isAdmin;
            UserIdentity = userIdentity;
            NewSyncJob = newSyncJob;
        }
        public bool IsAdmin { get; set; }
        public NewSyncJobDTO NewSyncJob { get; }
        public string UserIdentity { get; }

    }
}