// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using SettingDTO = WebApi.Models.DTOs.Setting;

namespace Services
{
    public class GetAllSettingsHandler : RequestHandlerBase<GetAllSettingsRequest, GetAllSettingsResponse>
    {
        private readonly IDatabaseSettingsRepository _databaseSettingsRepository;
        private readonly IConfiguration _configuration;

        public GetAllSettingsHandler(ILogger<GetAllSettingsHandler> logger,
                                IDatabaseSettingsRepository databaseSettingsRepository,
                                IConfiguration configuration) : base(logger)
        {
            _databaseSettingsRepository = databaseSettingsRepository ?? throw new ArgumentNullException(nameof(databaseSettingsRepository));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
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

            var isRunHistoryPhase2Enabled =
                bool.TryParse(_configuration[ConfigurationKeyNames.RunHistoryOpenViewingAndUnifiedTab], out var configuredValue)
                && configuredValue;

            response.Settings.Add(new SettingDTO(
                SettingKey.RunHistoryOpenViewingAndUnifiedTab,
                isRunHistoryPhase2Enabled ? "true" : "false"));

            return response;
        }
    }
}