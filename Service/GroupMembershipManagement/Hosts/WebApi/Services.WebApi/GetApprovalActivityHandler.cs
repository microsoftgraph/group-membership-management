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
    public class GetApprovalActivityHandler : RequestHandlerBase<GetApprovalActivityRequest, GetApprovalActivityResponse>
    {
        private readonly ILogger<GetApprovalActivityHandler> _logger;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;

        public GetApprovalActivityHandler(ILogger<GetApprovalActivityHandler> logger,
                                          IDatabaseSyncJobsRepository databaseSyncJobsRepository) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
        }

        protected override async Task<GetApprovalActivityResponse> ExecuteCoreAsync(GetApprovalActivityRequest request)
        {
            var activity = await _databaseSyncJobsRepository.GetApprovalActivityAsync(request.WindowDays);

            _logger.LogInformation(
                "Approval activity retrieved over {WindowDays} days with {Automated} automated, {Manual} manual and {Rejected} rejected decisions across {Reviewers} reviewers.",
                activity.WindowDays,
                activity.AutomatedTotal,
                activity.ManualTotal,
                activity.RejectedTotal,
                activity.Reviewers.Count);

            return new GetApprovalActivityResponse
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Activity = activity
            };
        }
    }
}
