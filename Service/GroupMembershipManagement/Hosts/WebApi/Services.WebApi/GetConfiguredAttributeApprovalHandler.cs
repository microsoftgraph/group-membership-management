// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Microsoft.Extensions.Logging;

namespace Services
{
    public class GetConfiguredAttributeApprovalHandler : RequestHandlerBase<GetConfiguredAttributeApprovalRequest, GetConfiguredAttributeApprovalResponse>
    {
        private readonly IDatabaseSettingsRepository _databaseSettingsRepository;

        public GetConfiguredAttributeApprovalHandler(
            ILogger<GetConfiguredAttributeApprovalHandler> logger,
            IDatabaseSettingsRepository databaseSettingsRepository) : base(logger)
        {
            _databaseSettingsRepository = databaseSettingsRepository ?? throw new ArgumentNullException(nameof(databaseSettingsRepository));
        }

        protected override async Task<GetConfiguredAttributeApprovalResponse> ExecuteCoreAsync(GetConfiguredAttributeApprovalRequest request)
        {
            return new GetConfiguredAttributeApprovalResponse
            {
                Configuration = await _databaseSettingsRepository.GetConfiguredAttributeApprovalAsync()
            };
        }
    }
}
