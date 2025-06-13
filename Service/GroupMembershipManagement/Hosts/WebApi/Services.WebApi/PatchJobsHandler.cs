// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace Services
{
    public class PatchJobsHandler : RequestHandlerBase<PatchJobsRequest, NullResponse>
    {
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        public PatchJobsHandler(ILoggingRepository loggingRepository,
                                IDatabaseSyncJobsRepository databaseSyncJobsRepository) : base(loggingRepository)
        {
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
        }

        protected override async Task<NullResponse> ExecuteCoreAsync(PatchJobsRequest request)
        {
            await _databaseSyncJobsRepository.BulkApproveSyncJobsAsync(request.SyncJobIds.ToList());
            return new NullResponse();
        }
    }
}