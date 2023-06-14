// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.SecurityGroup
{
    public class FeatureFlagFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IFeatureFlagRepository _featureFlagRepository;

        public FeatureFlagFunction(
            ILoggingRepository loggingRepository,
            IFeatureFlagRepository featureFlagRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _featureFlagRepository = featureFlagRepository ?? throw new ArgumentNullException(nameof(featureFlagRepository));
        }

        [FunctionName(nameof(FeatureFlagFunction))]
        public async Task<bool> CheckFeatureFlagStateAsync([ActivityTrigger] FeatureFlagRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(FeatureFlagFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            var isFlagEnabled = await _featureFlagRepository.IsFeatureFlagEnabledAsync(request.FeatureFlagName, request.RefreshAppConfigurationValues, request.RunId);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(FeatureFlagFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return isFlagEnabled;
        }
    }
}