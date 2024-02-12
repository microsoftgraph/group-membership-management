// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System.Threading.Tasks;
using Models.ThresholdNotifications;
using Models.Notifications;

namespace Hosts.Notifier
{
    public class OrchestratorFunction
    {
        public OrchestratorFunction()
        {
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

            var message = context.GetInput<OrchestratorRequest>();

            switch (message.MessageType)
            {
                case nameof(NotificationMessageType.ThresholdNotification):
                    var notification = await context.CallActivityAsync<ThresholdNotification>(nameof(CreateActionableNotificationFromContentFunction), message);
                    await context.CallActivityAsync(nameof(SendNotificationFunction), notification);
                    await context.CallActivityAsync(nameof(UpdateNotificationStatusFunction), new UpdateNotificationStatusRequest { Notification = notification, Status = ThresholdNotificationStatus.AwaitingResponse });
                    break;
                default:
                    await context.CallActivityAsync(nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        RunId = runId,
                        Message = $"{message.MessageType} is not a valid message type",
                        Verbosity = VerbosityLevel.DEBUG
                    });
                    break;
            }

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    RunId = runId,
                    Message = $"{nameof(OrchestratorFunction)} function completed at: {context.CurrentUtcDateTime}",
                    Verbosity = VerbosityLevel.DEBUG
                });
        }
    }
}