// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using SettingDTO = WebApi.Models.DTOs.Setting;

namespace Services{
    public class GetSettingHandler : RequestHandlerBase<GetSettingRequest, GetSettingResponse>
    {
        private readonly IDatabaseSettingsRepository _databaseSettingsRepository;
        public GetSettingHandler(ILoggingRepository loggingRepository, 
                                IDatabaseSettingsRepository databaseSettingsRepository) : base(loggingRepository)
        {
            _databaseSettingsRepository = databaseSettingsRepository ?? throw new ArgumentNullException(nameof(databaseSettingsRepository));
        }

        protected override async Task<GetSettingResponse> ExecuteCoreAsync(GetSettingRequest request)
        {
            var response = new GetSettingResponse();
            var setting = await _databaseSettingsRepository.GetSettingByKeyAsync(request.SettingKey);
            var dto = new SettingDTO(setting.SettingKey, setting.SettingValue);
            response.Model = dto;
            return response;
        }
    }
}