// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace Services
{
    public class PatchJobsHandler : RequestHandlerBase<PatchJobsRequest, PatchJobsResponse>
    {
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        public PatchJobsHandler(ILoggingRepository loggingRepository,
                                IDatabaseSyncJobsRepository databaseSyncJobsRepository) : base(loggingRepository)
        {
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
        }

        protected override async Task<PatchJobsResponse> ExecuteCoreAsync(PatchJobsRequest request)
        {
            var approvedCount = await _databaseSyncJobsRepository.BulkApproveSyncJobsAsync(request.SyncJobIds.ToList());
            return new PatchJobsResponse { ApprovedJobsCount = approvedCount };
        }
    }
}