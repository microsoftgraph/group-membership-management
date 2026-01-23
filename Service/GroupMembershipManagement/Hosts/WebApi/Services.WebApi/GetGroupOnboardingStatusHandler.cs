// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Common.DependencyInjection;
using Microsoft.Extensions.Options;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace Services
{
    public class GetGroupOnboardingStatusHandler : RequestHandlerBase<GetGroupOnboardingStatusRequest, GetOnboardingStatusResponse>
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly string _gmmAppId;
        private readonly string _gmmAppName;

        public GetGroupOnboardingStatusHandler(ILoggingRepository loggingRepository,
                              IGraphGroupRepository graphGroupRepository,
                              IDatabaseSyncJobsRepository syncJobRepository,
                              IOptions<GraphCredentials> graphCredentials) : base(loggingRepository)
        {
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _gmmAppId = graphCredentials.Value.GMMOwnerAppId;
            _gmmAppName = graphCredentials.Value.GMMOwnerAppName;
        }

        protected override async Task<GetOnboardingStatusResponse> ExecuteCoreAsync(GetGroupOnboardingStatusRequest request)
        {
            var groupExists = await _graphGroupRepository.GroupExists(request.GroupId);

            var isAppIdOwnerTask = groupExists
              ? _graphGroupRepository.IsAppIDOwnerOfGroup(_gmmAppId, request.GroupId, validateGroupExists: false)
              : Task.FromResult(false);

            var isUserOwnerTask = groupExists
                ? _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(request.UserIdentity, request.GroupId, validateGroupExists: false)
                : Task.FromResult(false);

            var isSyncedOnPremisesTask = groupExists
                ? _graphGroupRepository.IsGroupSyncedOnPremisesAsync(request.GroupId)
                : Task.FromResult(false);

            var syncJobTask = _syncJobRepository.GetSyncJobByObjectIdAsync(request.GroupId);

            await Task.WhenAll(isAppIdOwnerTask, isUserOwnerTask, isSyncedOnPremisesTask, syncJobTask);

            var isAppIdOwner = await isAppIdOwnerTask;
            var isUserOwner = await isUserOwnerTask;
            var isSyncedOnPremises = await isSyncedOnPremisesTask;
            var isOnboarded = await syncJobTask != null;

            var response = new GetOnboardingStatusResponse();

            if (isOnboarded)
            {
                response.Status = OnboardingStatus.Onboarded;
            }
            else if (isSyncedOnPremises)
            {
                response.Status = OnboardingStatus.SyncedOnPremises;
            }
            else if (!isAppIdOwner)
            {
                response.Status = OnboardingStatus.GmmNotOwner;
                response.AdditionalDetails = new Dictionary<string, string>
                {
                    { "owner", _gmmAppName }
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