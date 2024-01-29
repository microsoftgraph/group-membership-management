// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Common.DependencyInjection;
using Microsoft.Extensions.Options;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using WebApi.Models.DTOs;

namespace Services
{
    public class GetGroupOnboardingStatusHandler : RequestHandlerBase<GetGroupOnboardingStatusRequest, GetGroupOnboardingStatusResponse>
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ISyncJobRepository _syncJobRepository;
        private readonly string _gmmAppId;

        public GetGroupOnboardingStatusHandler(ILoggingRepository loggingRepository,
                              IGraphGroupRepository graphGroupRepository,
                              ISyncJobRepository syncJobRepository,
                              IOptions<GraphCredentials> graphCredentials) : base(loggingRepository)
        {
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _gmmAppId = graphCredentials.Value.ClientId;
        }

        protected override async Task<GetGroupOnboardingStatusResponse> ExecuteCoreAsync(GetGroupOnboardingStatusRequest request)
        {
            var isAppIdOwner = await _graphGroupRepository.IsAppIDOwnerOfGroup(_gmmAppId, request.GroupId);
            var syncJobExists = await _syncJobRepository.GetSyncJobByObjectIdAsync(request.GroupId);
            bool isOnboarded = syncJobExists != null;

            var response = new GetGroupOnboardingStatusResponse();

            if (isOnboarded)
            {
                response.Status = OnboardingStatus.Onboarded;
            }
            else if (isAppIdOwner && !isOnboarded)
            {
                response.Status = OnboardingStatus.ReadyForOnboarding;
            }
            else
            {
                response.Status = OnboardingStatus.NotReadyForOnboarding;
            }

            return response;
        }

    }
}