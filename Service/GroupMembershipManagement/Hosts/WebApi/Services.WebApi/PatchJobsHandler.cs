// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.SyncJobChange;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace Services
{
    public class PatchJobsHandler : RequestHandlerBase<PatchJobsRequest, PatchJobsResponse>
    {
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly ISyncJobChangeRepository _syncJobChangeRepository;
        public PatchJobsHandler(ILoggingRepository loggingRepository,
                                IDatabaseSyncJobsRepository databaseSyncJobsRepository,
                                ISyncJobChangeRepository syncJobChangeRepository) : base(loggingRepository)
        {
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _syncJobChangeRepository = syncJobChangeRepository ?? throw new ArgumentNullException(nameof(syncJobChangeRepository));
        }

        protected override async Task<PatchJobsResponse> ExecuteCoreAsync(PatchJobsRequest request)
        {
            var approvedCount = await _databaseSyncJobsRepository.BulkApproveSyncJobsAsync(request.SyncJobIds.ToList());

            var syncJobChangeList = new List<SyncJobChange>();

            foreach (var syncJobId in request.SyncJobIds)
            {
                var syncJobChange = new SyncJobChange
                {
                    SyncJobId = Guid.Parse(syncJobId),
                    ChangeTime = DateTime.UtcNow,
                    ChangeReason = SyncJobChangeReason.SubmissionApproved.ToString(),
                    ChangedByObjectId = Guid.Parse(request.UserIdentity),
                    ChangedByDisplayName = request.UserDisplayName
                };

                syncJobChangeList.Add(syncJobChange);
            }

            await _syncJobChangeRepository.BulkSaveAsync(syncJobChangeList);

            return new PatchJobsResponse { ApprovedJobsCount = approvedCount };
        }
    }
}