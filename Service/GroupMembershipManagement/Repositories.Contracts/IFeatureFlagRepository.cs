// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IFeatureFlagRepository
    {
        Task<bool> IsFeatureFlagEnabledAsync(string featureFlagName, bool refreshAppConfigurationValues, Guid? runId);
    }
}
