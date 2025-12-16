// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.MembershipAggregator.Helpers
{
    public static class MembershipFilePathHelper
    {
        public static string BuildFilePath(SyncJob syncJob, Guid groupId, string suffix, DateTime currentUtcDateTime)
        {
            if (groupId == Guid.Empty)
            {
                throw new ArgumentException("Group identifier is required to build a membership file path.", nameof(groupId));
            }

            var timeStamp = currentUtcDateTime.ToString("MMddyyyy-HHmm");
            var runId = syncJob?.RunId.HasValue == true ? syncJob.RunId.Value.ToString() : string.Empty;

            return $"/{groupId}/{timeStamp}_{runId}_{suffix}.json";
        }
    }
}