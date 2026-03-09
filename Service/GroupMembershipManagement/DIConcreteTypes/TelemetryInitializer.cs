// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace DIConcreteTypes
{
    public sealed class TelemetryInitializer : ITelemetryInitializer
    {
        private const string CategoryNameProperty = "CategoryName";
        private readonly IReadOnlyList<string> _allowedPrefixes;

        public TelemetryInitializer(TelemetryInitializerConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            var allowedPrefixes = TelemetryInitializerConfig.DefaultAllowedPrefixes
                .Concat(config.AdditionalAllowedPrefixes.Where(prefix => !string.IsNullOrWhiteSpace(prefix)));

            _allowedPrefixes = allowedPrefixes.ToList();
        }

        public void Initialize(ITelemetry telemetry)
        {
            if (telemetry is not ISupportProperties supportProperties)
            {
                return;
            }

            if (!supportProperties.Properties.TryGetValue(CategoryNameProperty, out var categoryName)
                || string.IsNullOrWhiteSpace(categoryName))
            {
                return;
            }

            if (_allowedPrefixes.Any(prefix => categoryName.StartsWith(prefix, StringComparison.Ordinal)))
            {
                supportProperties.Properties[TelemetryConstants.LogSourceProperty] = TelemetryConstants.LogSource;
            }
        }
    }
}
