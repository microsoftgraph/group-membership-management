// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.FeatureManagement;
using Models;
using Repositories.Contracts;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.SecurityGroup
{
    public class FeatureFlagFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IFeatureManager _featureManager;
        private readonly IConfigurationRefresherProvider _refresherProvider;

        public FeatureFlagFunction(
            ILoggingRepository loggingRepository,
            IFeatureManager featureManager,
            IConfigurationRefresherProvider refresherProvider)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _featureManager = featureManager ?? throw new ArgumentNullException(nameof(featureManager));
            _refresherProvider = refresherProvider ?? throw new ArgumentNullException(nameof(refresherProvider));
        }

        [FunctionName(nameof(FeatureFlagFunction))]
        public async Task<bool> CheckFeatureFlagStateAsync([ActivityTrigger] FeatureFlagRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(FeatureFlagFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            if (request.RefreshAppConfigurationValues)
            {
                var refresher = _refresherProvider.Refreshers.First();
                if (!await refresher.TryRefreshAsync())
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    { Message = $"Unable to refresh app configuration values", RunId = request.RunId },
                    VerbosityLevel.DEBUG);
                }
            }

            var isFlagEnabled = await _featureManager.IsEnabledAsync(request.FeatureFlagName);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Feature flag {request.FeatureFlagName} is {(isFlagEnabled ? "enabled" : "disabled")}", RunId = request.RunId }, VerbosityLevel.INFO);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(FeatureFlagFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return isFlagEnabled;
        }
    }
}