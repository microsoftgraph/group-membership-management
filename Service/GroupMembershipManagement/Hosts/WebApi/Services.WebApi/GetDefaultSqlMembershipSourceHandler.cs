// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using SettingDTO = WebApi.Models.DTOs.Setting;

namespace Services
{
    public class GetDefaultSqlMembershipSourceHandler : RequestHandlerBase<GetDefaultSqlMembershipSourceRequest, GetDefaultSqlMembershipSourceResponse>
    {
        private readonly IDatabaseSqlMembershipSourcesRepository _databaseSqlMembershipSourcesRepository;
        public GetDefaultSqlMembershipSourceHandler(ILoggingRepository loggingRepository,
                                IDatabaseSqlMembershipSourcesRepository databaseSqlMembershipSourcesRepository) : base(loggingRepository)
        {
            _databaseSqlMembershipSourcesRepository = databaseSqlMembershipSourcesRepository ?? throw new ArgumentNullException(nameof(databaseSqlMembershipSourcesRepository));
        }

        protected override async Task<GetDefaultSqlMembershipSourceResponse> ExecuteCoreAsync(GetDefaultSqlMembershipSourceRequest request)
        {
            var source = await _databaseSqlMembershipSourcesRepository.GetDefaultSourceAsync();
            var response = new GetDefaultSqlMembershipSourceResponse();
            response.Model = source;
            return response;
        }
    }
}