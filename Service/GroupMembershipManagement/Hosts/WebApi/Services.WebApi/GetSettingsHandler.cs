// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using SettingsDTO = WebApi.Models.DTOs.Settings;

namespace Services{
    public class GetSettingsHandler : RequestHandlerBase<GetSettingsRequest, GetSettingsResponse>
    {
        private readonly IDatabaseSettingsRepository _databaseSettingsRepository;
        public GetSettingsHandler(ILoggingRepository loggingRepository, 
                                IDatabaseSettingsRepository databaseSettingsRepository) : base(loggingRepository)
        {
            _databaseSettingsRepository = databaseSettingsRepository ?? throw new ArgumentNullException(nameof(databaseSettingsRepository));
        }

        protected override async Task<GetSettingsResponse> ExecuteCoreAsync(GetSettingsRequest request)
        {
            var response = new GetSettingsResponse();
            Settings settings = await _databaseSettingsRepository.GetSettingsAsync(request.Key);

            var dto = new SettingsDTO
                (
                    key: settings.Key,
                    value: settings.Value
                );

            response.Model = dto;

            return response;
        }
    }
}