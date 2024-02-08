// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace Services
{
    public class GetOrgLeaderDetailsHandler : RequestHandlerBase<GetOrgLeaderDetailsRequest, GetOrgLeaderDetailsResponse>
    {
        private readonly IDataFactoryRepository _dataFactoryRepository;
        private readonly ISqlMembershipRepository _sqlMembershipRepository;
        private readonly ILoggingRepository _loggingRepository;

        public GetOrgLeaderDetailsHandler(ILoggingRepository loggingRepository,
                                ISqlMembershipRepository sqlMembershipRepository,
                                IDataFactoryRepository dataFactoryRepository) : base(loggingRepository)
        {
            _dataFactoryRepository = dataFactoryRepository ?? throw new ArgumentNullException(nameof(dataFactoryRepository));
            _sqlMembershipRepository = sqlMembershipRepository ?? throw new ArgumentNullException(nameof(sqlMembershipRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        protected override async Task<GetOrgLeaderDetailsResponse> ExecuteCoreAsync(GetOrgLeaderDetailsRequest request)
        {
            var response = new GetOrgLeaderDetailsResponse();
            var adfRunId = await _dataFactoryRepository.GetMostRecentSucceededRunIdAsync();
            var tableName = string.Concat("tbl", adfRunId.Replace("-", ""));
            var (maxDepth, employeeId) = await _sqlMembershipRepository.GetOrgLeaderDetailsAsync(request.ObjectId, tableName);
            response.MaxDepth = maxDepth;
            response.EmployeeId = employeeId;
            return response;
        }
    }
}