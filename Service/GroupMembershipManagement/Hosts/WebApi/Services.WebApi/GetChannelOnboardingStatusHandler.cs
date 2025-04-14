// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Common.DependencyInjection;
using Microsoft.Extensions.Options;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace Services
{
    public class GetChannelOnboardingStatusHandler : RequestHandlerBase<GetChannelOnboardingStatusRequest, GetOnboardingStatusResponse>
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ITeamsChannelRepository _teamsChannelRepository;
        private readonly ITeamsChannelConfig _teamsChannelConfig;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly string _gmmAppId;

        public GetChannelOnboardingStatusHandler(ILoggingRepository loggingRepository,
                              IGraphGroupRepository graphGroupRepository,
                              ITeamsChannelRepository teamsChannelRepository,
                              ITeamsChannelConfig teamsChannelConfig,
                              IDatabaseSyncJobsRepository syncJobRepository,
                              IOptions<GraphCredentials> graphCredentials) : base(loggingRepository)
        {
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _teamsChannelRepository = teamsChannelRepository ?? throw new ArgumentNullException(nameof(teamsChannelRepository));
            _teamsChannelConfig = teamsChannelConfig ?? throw new ArgumentNullException(nameof(teamsChannelConfig));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _gmmAppId = graphCredentials.Value.GMMOwnerAppId;
        }

        protected override async Task<GetOnboardingStatusResponse> ExecuteCoreAsync(GetChannelOnboardingStatusRequest request)
        {
            var azureADChannel = new Models.Entities.AzureADTeamsChannel
            {
                ObjectId = request.TeamId,
                ChannelId = request.ChannelId
            };

            var isServiceAccountOwner = await _teamsChannelRepository.IsServiceAccountOwnerOfChannelAsync(_teamsChannelConfig.TeamsChannelServiceAccountObjectId, azureADChannel, null);
            var isUserOwner = await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(request.UserIdentity, request.TeamId);
            var syncJobExists = await _syncJobRepository.GetSyncJobByObjectIdAsync(request.TeamId);
            bool isOnboarded = syncJobExists != null;

            var response = new GetOnboardingStatusResponse();

            if (isOnboarded)
            {
                response.Status = OnboardingStatus.Onboarded;
            }
            else if (!isServiceAccountOwner)
            {
                response.Status = OnboardingStatus.GmmNotOwner;
            }
            else if (!isUserOwner)
            {
                if (request.IsJobTenantWriter)
                {
                    response.Status = OnboardingStatus.ReadyForOnboarding;
                }
                else
                {
                    response.Status = OnboardingStatus.UserNotOwner;
                }
            }
            else
            {
                response.Status = OnboardingStatus.ReadyForOnboarding;
            }

            return response;
        }

    }
}