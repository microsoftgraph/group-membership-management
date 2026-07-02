// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Microsoft.Extensions.Logging;
using SettingDTO = WebApi.Models.DTOs.Setting;

namespace Services{
    public class GetSettingHandler : RequestHandlerBase<GetSettingRequest, GetSettingResponse>
    {
        private readonly IDatabaseSettingsRepository _databaseSettingsRepository;
        public GetSettingHandler(ILogger<GetSettingHandler> logger, 
                                IDatabaseSettingsRepository databaseSettingsRepository) : base(logger)
        {
            _databaseSettingsRepository = databaseSettingsRepository ?? throw new ArgumentNullException(nameof(databaseSettingsRepository));
        }

        protected override async Task<GetSettingResponse> ExecuteCoreAsync(GetSettingRequest request)
        {
            var response = new GetSettingResponse();
            var setting = await _databaseSettingsRepository.GetSettingByKeyAsync(request.SettingKey);
            if (setting == null)
            {
                response.Model = null;
                return response;
            }
            var dto = new SettingDTO(setting.SettingKey, setting.SettingValue);
            response.Model = dto;
            return response;
        }
    }
}