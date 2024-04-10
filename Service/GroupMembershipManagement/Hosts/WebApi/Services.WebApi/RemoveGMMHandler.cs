// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Common.DependencyInjection;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.Extensions.Options;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi;
using System.Net;
using WebApi.Models.DTOs;

namespace Services
{
    public class RemoveGMMHandler : RequestHandlerBase<RemoveGMMRequest, RemoveGMMResponse>
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly PatchJobHandler _patchJobHandler;

        public RemoveGMMHandler(ILoggingRepository loggingRepository,
                              IGraphGroupRepository graphGroupRepository,
                              IDatabaseSyncJobsRepository syncJobRepository,
                              PatchJobHandler patchJobHandler) : base(loggingRepository)
        {
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _patchJobHandler = patchJobHandler ?? throw new ArgumentNullException(nameof(patchJobHandler));
        }

        protected override async Task<RemoveGMMResponse> ExecuteCoreAsync(RemoveGMMRequest request)
        {

            var syncJob = await _syncJobRepository.GetSyncJobAsync(request.SyncJobId);
            if (syncJob == null)
            {
                return new RemoveGMMResponse
                {
                    StatusCode = HttpStatusCode.NotFound
                };
            }

            var isOwner = await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(request.UserIdentity, syncJob.TargetOfficeGroupId);
            if (!(isOwner || request.IsJobTenantWriter))
            {
                return new RemoveGMMResponse
                {
                    StatusCode = HttpStatusCode.Forbidden
                };
            }

            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(e => e.Status, "Removed");

            var patchJobRequest = new PatchJobRequest(true, request.UserIdentity, request.SyncJobId, patchDocument);
            var patchJobResponse = await _patchJobHandler.ExecuteAsync(patchJobRequest);

            if (patchJobResponse.StatusCode != HttpStatusCode.OK)
            {
                return new RemoveGMMResponse
                {
                    StatusCode = patchJobResponse.StatusCode,
                    ErrorCode = patchJobResponse.ErrorCode
                };
            }

            return new RemoveGMMResponse
            {
                StatusCode = HttpStatusCode.OK
            };
        }

    }
}