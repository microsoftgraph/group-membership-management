// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace Services
{
    public class UpdateSettingHandler : RequestHandlerBase<UpdateSettingRequest, NullResponse>
    {
        private readonly IDatabaseSettingsRepository _databaseSettingsRepository;
        public UpdateSettingHandler(ILoggingRepository loggingRepository,
                                IDatabaseSettingsRepository databaseSettingsRepository) : base(loggingRepository)
        {
            _databaseSettingsRepository = databaseSettingsRepository ?? throw new ArgumentNullException(nameof(databaseSettingsRepository));
        }

        protected override async Task<NullResponse> ExecuteCoreAsync(UpdateSettingRequest request)
        {
            var setting = await _databaseSettingsRepository.GetSettingByKeyAsync(request.Key);
            await _databaseSettingsRepository.UpdateSettingAsync(setting, request.Value);
            return new NullResponse();
        }
    }
}