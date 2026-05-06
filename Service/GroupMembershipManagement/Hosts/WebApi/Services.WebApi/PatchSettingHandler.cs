// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Microsoft.Extensions.Logging;

namespace Services
{
    public class PatchSettingHandler : RequestHandlerBase<PatchSettingRequest, NullResponse>
    {
        private readonly IDatabaseSettingsRepository _databaseSettingsRepository;
        public PatchSettingHandler(ILogger<PatchSettingHandler> logger,
                                IDatabaseSettingsRepository databaseSettingsRepository) : base(logger)
        {
            _databaseSettingsRepository = databaseSettingsRepository ?? throw new ArgumentNullException(nameof(databaseSettingsRepository));
        }

        protected override async Task<NullResponse> ExecuteCoreAsync(PatchSettingRequest request)
        {
            await _databaseSettingsRepository.PatchSettingAsync(request.SettingKey, request.SettingValue);
            return new NullResponse();
        }
    }
}