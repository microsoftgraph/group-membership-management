// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using ConfigurationDTO = WebApi.Models.DTOs.Configuration;

namespace Services{
    public class GetConfigurationHandler : RequestHandlerBase<GetConfigurationRequest, GetConfigurationResponse>
    {
        private readonly IDatabaseConfigurationRepository _databaseConfigurationRepository;
        public GetConfigurationHandler(ILoggingRepository loggingRepository, 
                                IDatabaseConfigurationRepository databaseConfigurationRepository) : base(loggingRepository)
        {
            _databaseConfigurationRepository = databaseConfigurationRepository ?? throw new ArgumentNullException(nameof(databaseConfigurationRepository));
        }

        protected override async Task<GetConfigurationResponse> ExecuteCoreAsync(GetConfigurationRequest request)
        {
            var response = new GetConfigurationResponse();
            Configuration configuration = await _databaseConfigurationRepository.GetConfigurationAsync(request.Id);

            var dto = new ConfigurationDTO
                (
                    id: configuration.Id,
                    dashboardUrl: configuration.DashboardUrl
                );

            response.Model = dto;

            return response;
        }
    }
}