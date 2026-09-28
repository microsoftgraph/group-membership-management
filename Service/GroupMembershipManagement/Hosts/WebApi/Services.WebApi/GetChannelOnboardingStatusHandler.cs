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
using Microsoft.Extensions.Logging;

namespace Services
{
    public class GetChannelOnboardingStatusHandler : RequestHandlerBase<GetChannelOnboardingStatusRequest, GetOnboardingStatusResponse>
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ITeamsChannelRepository _teamsChannelRepository;
        private readonly ITeamsChannelConfig _teamsChannelConfig;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly string _teamsChannelOwner;

        public GetChannelOnboardingStatusHandler(ILogger<GetChannelOnboardingStatusHandler> logger,
                              IGraphGroupRepository graphGroupRepository,
                              ITeamsChannelRepository teamsChannelRepository,
                              ITeamsChannelConfig teamsChannelConfig,
                              IDatabaseSyncJobsRepository syncJobRepository,
                              IOptions<GraphCredentials> graphCredentials) : base(logger)
        {
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _teamsChannelRepository = teamsChannelRepository ?? throw new ArgumentNullException(nameof(teamsChannelRepository));
            _teamsChannelConfig = teamsChannelConfig ?? throw new ArgumentNullException(nameof(teamsChannelConfig));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _teamsChannelOwner = teamsChannelConfig.GMMHasTeamsChannelApplicationPermissions
                                 ? teamsChannelConfig.TeamsChannelAppRegistrationName
                                 : teamsChannelConfig.TeamsChannelServiceAccountUsername;
        }

        protected override async Task<GetOnboardingStatusResponse> ExecuteCoreAsync(GetChannelOnboardingStatusRequest request)
        {
            // US6 defense-in-depth: the controller gates this onboarding endpoint; re-check here so the
            // handler cannot compute onboarding status when the effective authorization is unmet.
            if (!request.IsOnboardingAuthorized)
            {
                throw new UnauthorizedAccessException("TeamsChannel onboarding requires the TeamsChannel onboarder permission.");
            }

            var azureADChannel = new Models.Entities.AzureADTeamsChannel
            {
                ObjectId = request.TeamId,
                ChannelId = request.ChannelId
            };

            var isServiceAccountOwner = await _teamsChannelRepository.IsServiceAccountOwnerOfChannelAsync(_teamsChannelConfig.TeamsChannelServiceAccountObjectId, azureADChannel, null);
            var isUserOwner = await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(request.UserIdentity, request.TeamId);
            // Channel-level lookup: a channel is "Onboarded" only when a TeamsChannel SyncJob exists for the
            // exact (TeamId, ChannelId) pair. A GroupMembership job (or an unrelated sibling channel job) on the
            // same Team no longer blocks this channel (US1 coexistence, US2 sibling channels).
            var syncJobExists = await _syncJobRepository.GetSyncJobByTeamIdAndChannelIdAsync(request.TeamId, request.ChannelId);
            bool isOnboarded = syncJobExists != null;

            var response = new GetOnboardingStatusResponse();

            if (isOnboarded)
            {
                response.Status = OnboardingStatus.Onboarded;
            }
            else if (!isServiceAccountOwner)
            {
                response.Status = OnboardingStatus.GmmNotOwner;
                response.AdditionalDetails = new Dictionary<string, string>
                {
                   { "owner", _teamsChannelOwner}
                };
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