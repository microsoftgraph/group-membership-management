// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace Services
{
    public class PatchSettingHandler : RequestHandlerBase<PatchSettingRequest, NullResponse>
    {
        private readonly IDatabaseSettingsRepository _databaseSettingsRepository;
        public PatchSettingHandler(ILoggingRepository loggingRepository,
                                IDatabaseSettingsRepository databaseSettingsRepository) : base(loggingRepository)
        {
            _databaseSettingsRepository = databaseSettingsRepository ?? throw new ArgumentNullException(nameof(databaseSettingsRepository));
        }

        protected override async Task<NullResponse> ExecuteCoreAsync(PatchSettingRequest request)
        {
            await _databaseSettingsRepository.PatchSettingAsync(request.SettingKey, request.Value);
            return new NullResponse();
        }
    }
}