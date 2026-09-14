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
    public class GetDashboardSummaryHandler : RequestHandlerBase<GetDashboardSummaryRequest, GetDashboardSummaryResponse>
    {
        private readonly ILogger<GetDashboardSummaryHandler> _logger;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;

        public GetDashboardSummaryHandler(ILogger<GetDashboardSummaryHandler> logger,
                                          IDatabaseSyncJobsRepository databaseSyncJobsRepository) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
        }

        protected override async Task<GetDashboardSummaryResponse> ExecuteCoreAsync(GetDashboardSummaryRequest request)
        {
            var summary = await _databaseSyncJobsRepository.GetDashboardSummaryAsync();

            _logger.LogInformation(
                "Dashboard summary retrieved with {PendingReviewBuckets} pending review buckets, {InProgressBuckets} in progress buckets and {AnomalousBuckets} anomalous status buckets.",
                summary.PendingReview.Count,
                summary.InProgress.Count,
                summary.AnomalousStatuses.Count);

            return new GetDashboardSummaryResponse
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Summary = summary
            };
        }
    }
}
