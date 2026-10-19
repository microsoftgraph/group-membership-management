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
    public class GetOrgLeaderHandler : RequestHandlerBase<GetOrgLeaderRequest, GetOrgLeaderResponse>
    {
        private readonly IAdfRunHistoryRepository _adfRunHistoryRepository;
        private readonly ISqlMembershipRepository _sqlMembershipRepository;

        public GetOrgLeaderHandler(ILogger<GetOrgLeaderHandler> logger,
                                ISqlMembershipRepository sqlMembershipRepository,
                                IAdfRunHistoryRepository adfRunHistoryRepository) : base(logger)
        {
            _adfRunHistoryRepository = adfRunHistoryRepository ?? throw new ArgumentNullException(nameof(adfRunHistoryRepository));
            _sqlMembershipRepository = sqlMembershipRepository ?? throw new ArgumentNullException(nameof(sqlMembershipRepository));
        }

        protected override async Task<GetOrgLeaderResponse> ExecuteCoreAsync(GetOrgLeaderRequest request)
        {
            var response = new GetOrgLeaderResponse();
            var tableName = (await _adfRunHistoryRepository.GetLatestAdfRunAsync() ?? throw new InvalidOperationException(AdfRunHistory.NoEligibleRunMessage)).GetTableName();
            var (maxDepth, azureObjectId) = await _sqlMembershipRepository.GetOrgLeaderAsync(request.EmployeeId, tableName);
            response.MaxDepth = maxDepth;
            response.AzureObjectId = azureObjectId;
            return response;
        }
    }
}