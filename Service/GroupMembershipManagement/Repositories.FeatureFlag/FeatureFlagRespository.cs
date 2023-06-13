// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.FeatureManagement;
using Models;
using Repositories.Contracts;

namespace Repositories.FeatureFlag
{
    public class FeatureFlagRespository : IFeatureFlagRespository
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IFeatureManager _featureManager;
        private readonly IConfigurationRefresherProvider _refresherProvider;

        public FeatureFlagRespository(
            ILoggingRepository loggingRepository,
            IFeatureManager featureManager,
            IConfigurationRefresherProvider refresherProvider)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
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
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    { Message = $"Unable to refresh app configuration values", RunId = runId },
                    VerbosityLevel.DEBUG);
                }
            }

            var isFlagEnabled = await _featureManager.IsEnabledAsync(featureFlagName);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Feature flag {featureFlagName} is {(isFlagEnabled ? "enabled" : "disabled")}",
                RunId = runId
            },
            VerbosityLevel.INFO);

            return isFlagEnabled;
        }
    }
}