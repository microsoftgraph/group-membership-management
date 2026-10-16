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
    public class SaveConfiguredAttributeApprovalHandler : RequestHandlerBase<SaveConfiguredAttributeApprovalRequest, SaveConfiguredAttributeApprovalResponse>
    {
        private readonly IDatabaseSettingsRepository _databaseSettingsRepository;
        private readonly IRequestHandler<GetDefaultSqlMembershipSourceAttributesRequest, GetDefaultSqlMembershipSourceAttributesResponse> _attributesHandler;
        private readonly IRequestHandler<ResolveDefaultSqlMembershipSourceAttributeMappingsRequest, ResolveDefaultSqlMembershipSourceAttributeMappingsResponse> _resolveMappingsHandler;

        public SaveConfiguredAttributeApprovalHandler(
            ILogger<SaveConfiguredAttributeApprovalHandler> logger,
            IDatabaseSettingsRepository databaseSettingsRepository,
            IRequestHandler<GetDefaultSqlMembershipSourceAttributesRequest, GetDefaultSqlMembershipSourceAttributesResponse> attributesHandler,
            IRequestHandler<ResolveDefaultSqlMembershipSourceAttributeMappingsRequest, ResolveDefaultSqlMembershipSourceAttributeMappingsResponse> resolveMappingsHandler) : base(logger)
        {
            _databaseSettingsRepository = databaseSettingsRepository ?? throw new ArgumentNullException(nameof(databaseSettingsRepository));
            _attributesHandler = attributesHandler ?? throw new ArgumentNullException(nameof(attributesHandler));
            _resolveMappingsHandler = resolveMappingsHandler ?? throw new ArgumentNullException(nameof(resolveMappingsHandler));
        }

        protected override async Task<SaveConfiguredAttributeApprovalResponse> ExecuteCoreAsync(SaveConfiguredAttributeApprovalRequest request)
        {
            var configuration = request.Configuration;
            configuration.Validate();
            if (configuration.Attributes.Count > 0)
            {
                var attributes = (await _attributesHandler.ExecuteAsync(new GetDefaultSqlMembershipSourceAttributesRequest())).Attributes;
                foreach (var row in configuration.Attributes)
                {
                    var attribute = attributes.SingleOrDefault(candidate => candidate.Name == row.Attribute);
                    if (!ConfiguredAttributeApprovalMetadata.IsEligible(attribute))
                        throw new ArgumentException("The selected attribute is unavailable, disabled, sensitive, or unsupported.");

                    var canonicalValues = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var value in row.Values)
                    {
                        if (!ConfiguredAttributeApprovalMetadata.TryCanonicalize(value, attribute.Type, out var canonical)
                            || !canonicalValues.Add(canonical))
                            throw new ArgumentException("A selected value is invalid or duplicated for its source type.");
                    }

                    if (!attribute.HasMapping)
                        throw new ArgumentException("Only attributes with mapped values can be configured.");

                    var options = (await _resolveMappingsHandler.ExecuteAsync(
                        new ResolveDefaultSqlMembershipSourceAttributeMappingsRequest(attribute.Name, row.Values))).Mappings;
                    var available = options.Select(option => option.Code).ToHashSet(StringComparer.Ordinal);
                    if (row.Values.Any(value => !available.Contains(value)))
                        throw new ArgumentException("A selected value is no longer available. Refresh the field's values.");
                }
            }

            return new SaveConfiguredAttributeApprovalResponse
            {
                Configuration = await _databaseSettingsRepository.SaveConfiguredAttributeApprovalAsync(configuration)
            };
        }
    }
}
