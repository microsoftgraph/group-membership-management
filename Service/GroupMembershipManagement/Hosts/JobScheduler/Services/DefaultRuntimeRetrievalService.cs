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

        public Task<Dictionary<string, double>> GetRunTimesInSecondsAsync()
        {
            Dictionary<string, double> runtimes = new Dictionary<string, double>();

            runtimes.Add("Default", DEFAULT_RUNTIME_SECONDS);

            return Task.FromResult(runtimes);
        }
    }
}
