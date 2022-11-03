// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services
{
    public class DefaultRuntimeRetrievalService : IRuntimeRetrievalService
    {
        private readonly double DEFAULT_RUNTIME_SECONDS;

        public DefaultRuntimeRetrievalService(double defaultRuntimeSeconds)
        {
            DEFAULT_RUNTIME_SECONDS = defaultRuntimeSeconds;
        }

        public Task<Dictionary<Guid, double>> GetRunTimesInSecondsAsync(List<Guid> groupIds)
        {
            Dictionary<Guid, double> runtimes = new Dictionary<Guid, double>();

            runtimes.Add(Guid.Empty, DEFAULT_RUNTIME_SECONDS);

            return Task.FromResult(runtimes);
        }
    }
}
