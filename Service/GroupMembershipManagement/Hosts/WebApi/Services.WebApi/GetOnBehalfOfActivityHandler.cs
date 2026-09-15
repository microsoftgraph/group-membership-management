// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace Services.WebApi
{
    public class GetOnBehalfOfActivityHandler : RequestHandlerBase<GetOnBehalfOfActivityRequest, GetOnBehalfOfActivityResponse>
    {
        private readonly ILogger<GetOnBehalfOfActivityHandler> _logger;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;

        public GetOnBehalfOfActivityHandler(ILogger<GetOnBehalfOfActivityHandler> logger,
                                            IDatabaseSyncJobsRepository databaseSyncJobsRepository) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
        }

        protected override async Task<GetOnBehalfOfActivityResponse> ExecuteCoreAsync(GetOnBehalfOfActivityRequest request)
        {
            var activity = await _databaseSyncJobsRepository.GetOnBehalfOfActivityAsync(request.WindowDays);

            _logger.LogInformation(
                "On-behalf-of activity retrieved over {WindowDays} days with {Onboarding} onboarding and {Update} update requests across {Submitters} submitters.",
                activity.WindowDays,
                activity.OnboardingTotal,
                activity.UpdateTotal,
                activity.Submitters.Count);

            return new GetOnBehalfOfActivityResponse
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Activity = activity
            };
        }
    }
}
