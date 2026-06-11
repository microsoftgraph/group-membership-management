// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.SignalR;
using System.Diagnostics.CodeAnalysis;

namespace Services.WebApi
{
    [ExcludeFromCodeCoverage]
    public class SignalRService : Hub
    {
        public const string SyncHistorySearchProgressEvent = "SyncHistorySearchProgress";

        public Task SubscribeToSyncHistorySearchProgress(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return Task.CompletedTask;
            }

            return Groups.AddToGroupAsync(Context.ConnectionId, BuildSyncHistorySearchGroupName(requestId));
        }

        public Task UnsubscribeFromSyncHistorySearchProgress(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return Task.CompletedTask;
            }

            return Groups.RemoveFromGroupAsync(Context.ConnectionId, BuildSyncHistorySearchGroupName(requestId));
        }

        public static string BuildSyncHistorySearchGroupName(string requestId)
        {
            return $"sync-history-search:{requestId}";
        }
    }

    public class SyncHistorySearchProgressUpdate
    {
        public string RequestId { get; set; } = string.Empty;
        public Guid SyncJobId { get; set; }
        public Guid UserObjectId { get; set; }
        public int ProcessedRuns { get; set; }
        public int TotalRuns { get; set; }
        public int MatchingRuns { get; set; }
        public bool Completed { get; set; }
    }
}
