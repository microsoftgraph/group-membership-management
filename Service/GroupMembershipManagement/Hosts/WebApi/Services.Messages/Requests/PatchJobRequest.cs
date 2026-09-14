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
        public bool IsOnBehalfAllowed { get; set; }
        public Guid SyncJobId { get; }
        public string UserIdentity { get; }
        public JsonPatchDocument<SyncJobPatch> PatchDocument { get; }
        public string UserDisplayName { get; set; }
        public string ChangeReason { get; set; }
        public string BusinessJustification { get; set; }
        public bool CanApproveJob { get; set; }
        public string TitlesValue { get; set; }
        public bool HasTitlesOperation { get; set; }

        public PatchJobRequest(
            bool isAllowed,
            bool isOnBehalfAllowed,
            string userIdentity,
            Guid syncJobId, 
            JsonPatchDocument<SyncJobPatch> patchDocument, 
            string userDisplayName, 
            string changeReason, 
            string businessJustification,
            bool canApproveJob,
            string titlesValue = null,
            bool hasTitlesOperation = false)
        {
            IsAllowed = isAllowed;
            IsOnBehalfAllowed = isOnBehalfAllowed;
            UserIdentity = userIdentity;
            SyncJobId = syncJobId;
            PatchDocument = patchDocument;
            UserDisplayName = userDisplayName;
            ChangeReason = changeReason;
            BusinessJustification = businessJustification;
            CanApproveJob = canApproveJob;
            TitlesValue = titlesValue;
            HasTitlesOperation = hasTitlesOperation;
        }
    }
}
