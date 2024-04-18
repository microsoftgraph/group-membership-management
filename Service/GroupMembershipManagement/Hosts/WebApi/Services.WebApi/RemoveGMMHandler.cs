// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;
using LogMessage = Models.LogMessage;

namespace Services
{
    public class RemoveGMMHandler : RequestHandlerBase<RemoveGMMRequest, RemoveGMMResponse>
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly ILoggingRepository _loggingRepository;

        public RemoveGMMHandler(ILoggingRepository loggingRepository,
                              IGraphGroupRepository graphGroupRepository,
                              IDatabaseSyncJobsRepository syncJobRepository) : base(loggingRepository)
        {
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
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

            try
            {
                await _syncJobRepository.DeleteSyncJobAsync(syncJob);

                return new RemoveGMMResponse
                {
                    StatusCode = HttpStatusCode.OK
                };
            } catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error removing GMM from job:\n{ex.Message}",
                    RunId = null
                });

                return new RemoveGMMResponse
                {
                    StatusCode = HttpStatusCode.InternalServerError
                };
            }
        }

    }
}