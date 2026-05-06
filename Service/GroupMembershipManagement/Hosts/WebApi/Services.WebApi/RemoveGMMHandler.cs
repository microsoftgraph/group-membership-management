// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Hosts.WebApi;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;
using Microsoft.Extensions.Logging;

namespace Services
{
    public class RemoveGMMHandler : RequestHandlerBase<RemoveGMMRequest, RemoveGMMResponse>
    {
        private readonly ILogger<RemoveGMMHandler> _logger;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;

        public RemoveGMMHandler(ILogger<RemoveGMMHandler> logger,
                              IGraphGroupRepository graphGroupRepository,
                              IDatabaseSyncJobsRepository syncJobRepository) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
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


            var groupId = syncJob.MembershipType == MembershipTypes.TeamsChannelMembership.ToString()
                ? syncJob.Channel?.GroupId
                : syncJob.Group?.GroupId;

            if (groupId == null)
            {
                return new RemoveGMMResponse
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    ErrorCode = "GroupIdNotFound"
                };
            }

            var isOwner = await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(request.UserIdentity, (Guid) groupId);
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
                _logger.RemoveGMMFailed(ex);

                return new RemoveGMMResponse
                {
                    StatusCode = HttpStatusCode.InternalServerError
                };
            }
        }

    }
}