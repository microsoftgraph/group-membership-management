// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Repositories.Contracts.Helpers;

namespace DIConcreteTypes
{
    public sealed class TelemetryInitializer : ITelemetryInitializer
    {
        private static readonly string[] _categoryPropertyNames =
        [
            "CategoryName",
            "Category"
        ];
        private const string FunctionUserCategoryPrefix = "Function.";
        private const string FunctionUserCategorySuffix = ".User";
        private static readonly string[] _activityCorrelationProperties =
        {
            CorrelationActivity.RunIdPropertyName,
            CorrelationActivity.SyncJobIdPropertyName
        };
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

            var categoryName = GetCategoryName(supportProperties);
            if (IsAllowedCategory(categoryName))
            {
                supportProperties.Properties[TelemetryConstants.LogSourceProperty] = TelemetryConstants.LogSource;
            }

            var activity = Activity.Current;
            if (activity == null)
            {
                return;
            }

            foreach (var propertyName in _activityCorrelationProperties)
            {
                if (supportProperties.Properties.ContainsKey(propertyName))
                {
                    continue;
                }

                var value = activity.GetTagItem(propertyName)?.ToString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    value = activity.Baggage.FirstOrDefault(x => x.Key == propertyName).Value;
                }

                if (!string.IsNullOrWhiteSpace(value))
                {
                    supportProperties.Properties[propertyName] = value;
                }
            }
        }

        private static string GetCategoryName(ISupportProperties supportProperties)
        {
            foreach (var propertyName in _categoryPropertyNames)
            {
                if (supportProperties.Properties.TryGetValue(propertyName, out var categoryName)
                    && !string.IsNullOrWhiteSpace(categoryName))
                {
                    return categoryName;
                }
            }

            return null;
        }

        private bool IsAllowedCategory(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                return false;
            }

            return _allowedPrefixes.Any(prefix => categoryName.StartsWith(prefix, StringComparison.Ordinal))
                || (categoryName.StartsWith(FunctionUserCategoryPrefix, StringComparison.Ordinal)
                    && categoryName.EndsWith(FunctionUserCategorySuffix, StringComparison.Ordinal));
        }
    }
}
