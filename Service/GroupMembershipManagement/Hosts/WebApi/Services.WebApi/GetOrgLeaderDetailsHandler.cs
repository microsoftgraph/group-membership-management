// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Microsoft.Extensions.Logging;

namespace Services
{
    public class GetOrgLeaderDetailsHandler : RequestHandlerBase<GetOrgLeaderDetailsRequest, GetOrgLeaderDetailsResponse>
    {
        private readonly IDataFactoryRepository _dataFactoryRepository;
        private readonly ISqlMembershipRepository _sqlMembershipRepository;

        public GetOrgLeaderDetailsHandler(ILogger<GetOrgLeaderDetailsHandler> logger,
                                ISqlMembershipRepository sqlMembershipRepository,
                                IDataFactoryRepository dataFactoryRepository) : base(logger)
        {
            _dataFactoryRepository = dataFactoryRepository ?? throw new ArgumentNullException(nameof(dataFactoryRepository));
            _sqlMembershipRepository = sqlMembershipRepository ?? throw new ArgumentNullException(nameof(sqlMembershipRepository));
        }

        protected override async Task<GetOrgLeaderDetailsResponse> ExecuteCoreAsync(GetOrgLeaderDetailsRequest request)
        {
            var response = new GetOrgLeaderDetailsResponse();
            var adfRunId = await _dataFactoryRepository.GetMostRecentSucceededRunIdAsync();
            var tableName = adfRunId.Replace("-", "");
            var (maxDepth, employeeId) = await _sqlMembershipRepository.GetOrgLeaderDetailsAsync(request.ObjectId, tableName);
            response.MaxDepth = maxDepth;
            response.EmployeeId = employeeId;
            return response;
        }
    }
}