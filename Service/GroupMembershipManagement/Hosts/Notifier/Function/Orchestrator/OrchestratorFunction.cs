// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;
using Models.ThresholdNotifications;
using Microsoft.ApplicationInsights;
using System;

namespace Hosts.Notifier
{
    public class OrchestratorFunction
    {
        private readonly TelemetryClient _telemetryClient;
        public OrchestratorFunction(
            TelemetryClient telemetryClient)
        {
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var runId = context.NewGuid();

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    RunId = runId,
                    Message = $"{nameof(OrchestratorFunction)} function started at: {context.CurrentUtcDateTime}",
                    Verbosity = VerbosityLevel.DEBUG
                });

            var notifications = await context.CallActivityAsync<List<Models.ThresholdNotifications.ThresholdNotification>>(nameof(RetrieveNotificationsFunction), null);

            if (notifications != null && notifications.Count > 0)
            {
                foreach(var notification in notifications)
                {
                    await context.CallActivityAsync(nameof(UpdateNotificationStatusFunction), new UpdateNotificationStatusRequest { Notification = notification, Status = ThresholdNotificationStatus.Triggered });
                    await context.CallActivityAsync(nameof(SendNotificationFunction), notification);
                    await context.CallActivityAsync(nameof(UpdateNotificationStatusFunction), new UpdateNotificationStatusRequest { Notification = notification, Status = ThresholdNotificationStatus.AwaitingResponse });
                    TrackSentNotificationEvent(notification.TargetOfficeGroupId);
                }
            }

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    RunId = runId,
                    Message = $"{nameof(OrchestratorFunction)} function completed at: {context.CurrentUtcDateTime}",
                    Verbosity = VerbosityLevel.DEBUG
                });
        }

        private void TrackSentNotificationEvent(Guid groupId)
        {
            var sentNotificationEvent = new Dictionary<string, string>
            {
                { "TargetGroupId", groupId.ToString() }
            };
            _telemetryClient.TrackEvent("NotificationSent", sentNotificationEvent);
        }
    }
}