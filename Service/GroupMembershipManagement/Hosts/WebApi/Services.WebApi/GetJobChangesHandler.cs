// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Extensions.Logging;

namespace Services
{
    public class GetJobChangesHandler : RequestHandlerBase<GetJobChangesRequest, GetJobChangesResponse>
    {
        private readonly ILogger<GetJobChangesHandler> _logger;
        private readonly ISyncJobChangeRepository _syncJobChangesRepository;

        public GetJobChangesHandler(ILogger<GetJobChangesHandler> logger,
                                    ISyncJobChangeRepository syncJobChangesRepository) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _syncJobChangesRepository = syncJobChangesRepository ?? throw new ArgumentNullException(nameof(syncJobChangesRepository));
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
                _logger.JobChangesRetrievalFailed(ex);

                response.StatusCode = HttpStatusCode.InternalServerError;
            }

            return response;
        }
    }
}
