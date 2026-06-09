// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GroupOwnershipObtainer
{
    public class FeatureFlagFunction
    {
        private readonly ILogger<FeatureFlagFunction> _logger;
        private readonly IFeatureFlagRepository _featureFlagRepository;

        public FeatureFlagFunction(
            ILogger<FeatureFlagFunction> logger,
            IFeatureFlagRepository featureFlagRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _featureFlagRepository = featureFlagRepository ?? throw new ArgumentNullException(nameof(featureFlagRepository));
        }

        [Function(nameof(FeatureFlagFunction))]
        public async Task<bool> CheckFeatureFlagStateAsync([ActivityTrigger] FeatureFlagRequest request)
        {
            using var scope = _logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            });
            _logger.FunctionStarted(nameof(FeatureFlagFunction));

            var isFlagEnabled = await _featureFlagRepository.IsFeatureFlagEnabledAsync(request.FeatureFlagName, request.RefreshAppConfigurationValues, request.SyncJob?.RunId);

            _logger.FunctionCompleted(nameof(FeatureFlagFunction));
            return isFlagEnabled;
        }
    }
}