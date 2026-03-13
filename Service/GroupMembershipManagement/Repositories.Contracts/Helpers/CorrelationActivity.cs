// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Diagnostics;

namespace Repositories.Contracts.Helpers
{
    public static class CorrelationActivity
    {
        public const string RunIdPropertyName = "RunId";
        public const string SyncJobIdPropertyName = "SyncJobId";

        public static Activity StartSyncJobActivity(string operationName, SyncJob syncJob)
        {
            var activity = StartActivity(operationName);

            if (syncJob == null)
            {
                return activity;
            }

            SetProperty(activity, SyncJobIdPropertyName, syncJob.Id.ToString());

            if (syncJob.RunId.HasValue && syncJob.RunId.Value != Guid.Empty)
            {
                SetProperty(activity, RunIdPropertyName, syncJob.RunId.Value.ToString());
            }

            return activity;
        }

        public static Activity StartRunIdActivity(string operationName, Guid? runId)
        {
            var activity = StartActivity(operationName);

            if (runId.HasValue && runId.Value != Guid.Empty)
            {
                SetProperty(activity, RunIdPropertyName, runId.Value.ToString());
            }

            return activity;
        }

        public static Guid? ResolveRunId(Guid? runId = null, Guid? fallbackRunId = null)
        {
            if (runId.HasValue && runId.Value != Guid.Empty)
            {
                return runId.Value;
            }

            var currentRunId = TryParseGuid(GetCurrentProperty(RunIdPropertyName));
            if (currentRunId.HasValue)
            {
                return currentRunId.Value;
            }

            if (fallbackRunId.HasValue && fallbackRunId.Value != Guid.Empty)
            {
                return fallbackRunId.Value;
            }

            return null;
        }

        public static string GetCurrentProperty(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            var activity = Activity.Current;
            if (activity == null)
            {
                return null;
            }

            var tagValue = activity.GetTagItem(propertyName)?.ToString();
            if (!string.IsNullOrWhiteSpace(tagValue))
            {
                return tagValue;
            }

            foreach (var baggageItem in activity.Baggage)
            {
                if (string.Equals(baggageItem.Key, propertyName, StringComparison.Ordinal))
                {
                    return baggageItem.Value;
                }
            }

            return null;
        }

        private static Activity StartActivity(string operationName)
        {
            var activity = new Activity(operationName);
            activity.SetIdFormat(ActivityIdFormat.W3C);
            activity.Start();
            return activity;
        }

        private static void SetProperty(Activity activity, string propertyName, string value)
        {
            if (activity == null || string.IsNullOrWhiteSpace(propertyName) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (activity.GetTagItem(propertyName) == null)
            {
                activity.SetTag(propertyName, value);
            }

            var alreadyInBaggage = false;
            foreach (var baggageItem in activity.Baggage)
            {
                if (string.Equals(baggageItem.Key, propertyName, StringComparison.Ordinal)
                    && string.Equals(baggageItem.Value, value, StringComparison.Ordinal))
                {
                    alreadyInBaggage = true;
                    break;
                }
            }

            if (!alreadyInBaggage)
            {
                activity.AddBaggage(propertyName, value);
            }
        }

        private static Guid? TryParseGuid(string value)
        {
            return Guid.TryParse(value, out var parsedGuid) ? parsedGuid : null;
        }
    }
}
