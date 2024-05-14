// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace Services
{
    public class GetOrgLeaderHandler : RequestHandlerBase<GetOrgLeaderRequest, GetOrgLeaderResponse>
    {
        private readonly IDataFactoryRepository _dataFactoryRepository;
        private readonly ISqlMembershipRepository _sqlMembershipRepository;
        private readonly ILoggingRepository _loggingRepository;

        public GetOrgLeaderHandler(ILoggingRepository loggingRepository,
                                ISqlMembershipRepository sqlMembershipRepository,
                                IDataFactoryRepository dataFactoryRepository) : base(loggingRepository)
        {
            _dataFactoryRepository = dataFactoryRepository ?? throw new ArgumentNullException(nameof(dataFactoryRepository));
            _sqlMembershipRepository = sqlMembershipRepository ?? throw new ArgumentNullException(nameof(sqlMembershipRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        protected override async Task<GetOrgLeaderResponse> ExecuteCoreAsync(GetOrgLeaderRequest request)
        {
            var response = new GetOrgLeaderResponse();
            var adfRunId = await _dataFactoryRepository.GetMostRecentSucceededRunIdAsync();
            var tableName = adfRunId.Replace("-", "");
            var (maxDepth, azureObjectId) = await _sqlMembershipRepository.GetOrgLeaderAsync(request.EmployeeId, tableName);
            response.MaxDepth = maxDepth;
            response.AzureObjectId = azureObjectId;
            return response;
        }
    }
}