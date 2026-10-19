// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Microsoft.Extensions.Logging;
using Models.AdfRunHistory;

namespace Services
{
    public class GetOrgLeaderDetailsHandler : RequestHandlerBase<GetOrgLeaderDetailsRequest, GetOrgLeaderDetailsResponse>
    {
        private readonly IAdfRunHistoryRepository _adfRunHistoryRepository;
        private readonly ISqlMembershipRepository _sqlMembershipRepository;

        public GetOrgLeaderDetailsHandler(ILogger<GetOrgLeaderDetailsHandler> logger,
                                ISqlMembershipRepository sqlMembershipRepository,
                                IAdfRunHistoryRepository adfRunHistoryRepository) : base(logger)
        {
            _adfRunHistoryRepository = adfRunHistoryRepository ?? throw new ArgumentNullException(nameof(adfRunHistoryRepository));
            _sqlMembershipRepository = sqlMembershipRepository ?? throw new ArgumentNullException(nameof(sqlMembershipRepository));
        }

        protected override async Task<GetOrgLeaderDetailsResponse> ExecuteCoreAsync(GetOrgLeaderDetailsRequest request)
        {
            var response = new GetOrgLeaderDetailsResponse();
            var tableName = (await _adfRunHistoryRepository.GetLatestAdfRunAsync() ?? throw new InvalidOperationException(AdfRunHistory.NoEligibleRunMessage)).GetTableName();
            var (maxDepth, employeeId) = await _sqlMembershipRepository.GetOrgLeaderDetailsAsync(request.ObjectId, tableName);
            response.MaxDepth = maxDepth;
            response.EmployeeId = employeeId;
            return response;
        }
    }
}