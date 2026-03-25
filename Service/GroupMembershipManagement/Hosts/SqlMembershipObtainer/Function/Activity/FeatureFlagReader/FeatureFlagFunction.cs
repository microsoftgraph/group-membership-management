// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.SqlMembershipObtainer;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SqlMembershipObtainer
{
    public class FeatureFlagFunction
    {
        private readonly ILogger<FeatureFlagFunction> _logger;
        private readonly IFeatureFlagRepository _featureFlagRespository;

        public FeatureFlagFunction(
            ILogger<FeatureFlagFunction> logger,
            IFeatureFlagRepository featureFlagRespository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _featureFlagRespository = featureFlagRespository ?? throw new ArgumentNullException(nameof(featureFlagRespository));
        }

        [Function(nameof(FeatureFlagFunction))]
        public async Task<bool> CheckFeatureFlagStateAsync([ActivityTrigger] FeatureFlagRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            }))
            {
                _logger.FunctionStarted(nameof(FeatureFlagFunction));

                var isFlagEnabled = await _featureFlagRespository.IsFeatureFlagEnabledAsync(request.FeatureFlagName, request.RefreshAppConfigurationValues, request.SyncJob.RunId ?? Guid.Empty);

                _logger.FunctionCompleted(nameof(FeatureFlagFunction));
                return isFlagEnabled;
            }
        }
    }
}