// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.JsonPatch;
using Services.Messages.Contracts.Requests;
using WebApi.Models.DTOs;

namespace Services.Messages.Requests
{
    public class PatchJobRequest : RequestBase
    {
        public bool IsAllowed { get; set; }
        public Guid SyncJobId { get; }
        public string UserIdentity { get; }
        public JsonPatchDocument<SyncJobPatch> PatchDocument { get; }

        public PatchJobRequest(bool isAllowed, string userIdentity, Guid syncJobId, JsonPatchDocument<SyncJobPatch> patchDocument)
        {
            IsAllowed = isAllowed;
            UserIdentity = userIdentity;
            SyncJobId = syncJobId;
            PatchDocument = patchDocument;
        }
    }
}
