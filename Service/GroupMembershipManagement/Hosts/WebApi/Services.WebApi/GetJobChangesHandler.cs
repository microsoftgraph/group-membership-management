// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace Services
{
    public class GetJobChangesHandler : RequestHandlerBase<GetJobChangesRequest, GetJobChangesResponse>
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ISyncJobChangeRepository _syncJobChangesRepository;

        public GetJobChangesHandler(ILoggingRepository loggingRepository,
                                    ISyncJobChangeRepository syncJobChangesRepository) : base(loggingRepository)
        {
            _syncJobChangesRepository = syncJobChangesRepository ?? throw new ArgumentNullException(nameof(syncJobChangesRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        protected override async Task<GetJobChangesResponse> ExecuteCoreAsync(GetJobChangesRequest request)
        {
            var response = new GetJobChangesResponse();

            try
            {

                var changes = await _syncJobChangesRepository.GetPageBySyncJobId(request.SyncJobId,
                                                                                 request.StartPage,
                                                                                 request.PageSize,
                                                                                 request.SortBy,
                                                                                 request.SortAscending);

                response.Changes = changes;
                response.StatusCode = HttpStatusCode.OK;
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error getting job changes: {ex.Message}",
                });

                response.StatusCode = HttpStatusCode.InternalServerError;
            }

            return response;
        }
    }
}
