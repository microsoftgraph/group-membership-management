// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace Services
{
    public class GetJobsHandler : RequestHandlerBase<GetJobsRequest, GetJobsResponse>
    {
        public GetJobsHandler(ILoggingRepository loggingRepository) : base(loggingRepository)
        {
        }

        protected override async Task<GetJobsResponse> ExecuteCoreAsync(GetJobsRequest request)
        {
            var response = new GetJobsResponse();
            for (var i = 0; i < 10; i++)
            {
                response.Model.Add(new WebApi.Models.DTOs.SyncJob
                {
                    PartitionKey = i.ToString(),
                    RowKey = Guid.NewGuid().ToString()
                });
            }

            return await Task.FromResult(response);
        }
    }
}