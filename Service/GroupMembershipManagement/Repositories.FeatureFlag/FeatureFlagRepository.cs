// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;

namespace Repositories.FeatureFlag
{
    public class FeatureFlagRepository : IFeatureFlagRepository
    {
        private readonly ILogger<FeatureFlagRepository> _featureFlagRepositoryLogger;
        private readonly IFeatureManager _featureManager;
        private readonly IConfigurationRefresherProvider _refresherProvider;

        public FeatureFlagRepository(
            ILogger<FeatureFlagRepository> featureFlagRepositoryLogger,
            IFeatureManager featureManager,
            IConfigurationRefresherProvider refresherProvider)
        {
            _featureFlagRepositoryLogger = featureFlagRepositoryLogger ?? throw new ArgumentNullException(nameof(featureFlagRepositoryLogger));
            _featureManager = featureManager ?? throw new ArgumentNullException(nameof(featureManager));
            _refresherProvider = refresherProvider ?? throw new ArgumentNullException(nameof(refresherProvider));
        }

        public async Task<bool> IsFeatureFlagEnabledAsync(string featureFlagName, bool refreshAppConfigurationValues, Guid? runId)
        {
            if (refreshAppConfigurationValues)
            {
                var refresher = _refresherProvider.Refreshers.First();
                if (!await refresher.TryRefreshAsync())
                {
                    _featureFlagRepositoryLogger.LogInformationWithRunId(runId, "Unable to refresh app configuration values");
                }
            }

            var isFlagEnabled = await _featureManager.IsEnabledAsync(featureFlagName);

            _featureFlagRepositoryLogger.LogInformationWithRunId(runId, $"Feature flag {featureFlagName} is {(isFlagEnabled ? "enabled" : "disabled")}");

            return isFlagEnabled;
        }
    }
}
