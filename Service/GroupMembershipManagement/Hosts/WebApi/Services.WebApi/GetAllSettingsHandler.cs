// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Microsoft.Extensions.Logging;
using SettingDTO = WebApi.Models.DTOs.Setting;

namespace Services
{
    public class GetAllSettingsHandler : RequestHandlerBase<GetAllSettingsRequest, GetAllSettingsResponse>
    {
        private readonly IDatabaseSettingsRepository _databaseSettingsRepository;
        public GetAllSettingsHandler(ILogger<GetAllSettingsHandler> logger,
                                IDatabaseSettingsRepository databaseSettingsRepository) : base(logger)
        {
            _databaseSettingsRepository = databaseSettingsRepository ?? throw new ArgumentNullException(nameof(databaseSettingsRepository));
        }

        protected override async Task<GetAllSettingsResponse> ExecuteCoreAsync(GetAllSettingsRequest request)
        {
            var response = new GetAllSettingsResponse();

            var settings = await _databaseSettingsRepository.GetAllSettingsAsync();

            foreach(var setting in settings)
            {
                var dto = new SettingDTO
                (
                    setting.SettingKey,
                    setting.SettingValue
                );
                response.Settings.Add(dto);
            }

            return response;
        }
    }
}