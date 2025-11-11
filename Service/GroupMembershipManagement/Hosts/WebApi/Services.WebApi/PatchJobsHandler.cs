// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.SyncJobChange;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace Services
{
    public class PatchJobsHandler : RequestHandlerBase<PatchJobsRequest, PatchJobsResponse>
    {
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly ISyncJobChangeRepository _syncJobChangeRepository;
        private readonly IThresholdConfig _thresholdConfig;
        
        public PatchJobsHandler(ILoggingRepository loggingRepository,
                                IDatabaseSyncJobsRepository databaseSyncJobsRepository,
                                ISyncJobChangeRepository syncJobChangeRepository,
                                IThresholdConfig thresholdConfig) : base(loggingRepository)
        {
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _syncJobChangeRepository = syncJobChangeRepository ?? throw new ArgumentNullException(nameof(syncJobChangeRepository));
            _thresholdConfig = thresholdConfig ?? throw new ArgumentNullException(nameof(thresholdConfig));
        }

        protected override async Task<PatchJobsResponse> ExecuteCoreAsync(PatchJobsRequest request)
        {
            // Set ThresholdViolations to N-1 so notification is sent on next threshold hit
            var thresholdViolationsToSet = _thresholdConfig.NumberOfThresholdViolationsToNotify - 1;
            var approvedCount = await _databaseSyncJobsRepository.BulkApproveSyncJobsAsync(request.SyncJobIds.ToList(), thresholdViolationsToSet);

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