// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System.Threading.Tasks;
using Models.ThresholdNotifications;
using Models.Notifications;
using Services.Contracts;
using Hosts.AzureMaintenance.Activity.SendNormalThresholdNotification;
using Models;
using System.Text.Json;
using System.Collections.Generic;
using System;

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
            var message = context.GetInput<OrchestratorRequest>();
            var messageContent = JsonSerializer.Deserialize<Dictionary<string, Object>>(message.MessageBody);
            SyncJob job = ((JsonElement)messageContent["SyncJob"]).Deserialize<SyncJob>();
            Guid runId = (Guid)job.RunId;

            message.RunId = runId;
            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    RunId = runId,
                    Message = $"{nameof(OrchestratorFunction)} function started at: {context.CurrentUtcDateTime}",
                    Verbosity = VerbosityLevel.DEBUG
                });

            switch (message.MessageType)
            {
                case nameof(NotificationMessageType.ThresholdNotification):
                    var notification = await context.CallActivityAsync<ThresholdNotification>(nameof(CreateThresholdNotificationFunction), message);
                    await context.CallActivityAsync(nameof(SendThresholdNotification), notification);
                    await context.CallActivityAsync(nameof(UpdateNotificationStatusFunction), new UpdateNotificationStatusRequest { Notification = notification, Status = ThresholdNotificationStatus.AwaitingResponse });
                    break;

                case nameof(NotificationMessageType.NormalThresholdNotification):
                    await context.CallActivityAsync(nameof(SendNormalThresholdNotification), message);
                    break;

                case nameof(NotificationMessageType.SyncStartedNotification):
                    message.SubjectTemplate = NotificationConstants.OnboardingSubject;
                    message.ContentTemplate = NotificationConstants.SyncStartedContent;
                    await context.CallActivityAsync(nameof(SendNotification), message);
                    break;

                case nameof(NotificationMessageType.DestinationNotExistNotification):
                    message.SubjectTemplate = NotificationConstants.DisabledNotificationSubject;
                    message.ContentTemplate = NotificationConstants.DestinationNotExistContent;
                    await context.CallActivityAsync(nameof(SendNotification), message);
                    break;

                case nameof(NotificationMessageType.NotOwnerNotification):
                    message.SubjectTemplate = NotificationConstants.DisabledNotificationSubject;
                    message.ContentTemplate = NotificationConstants.NotOwnerContent;
                    await context.CallActivityAsync(nameof(SendNotification), message);
                    break;

                case nameof(NotificationMessageType.SyncCompletedNotification):
                    message.SubjectTemplate = NotificationConstants.OnboardingSubject;
                    message.ContentTemplate = NotificationConstants.SyncCompletedContent;
                    await context.CallActivityAsync(nameof(SendNotification), message);
                    break;

                case nameof(NotificationMessageType.NotValidSourceNotification):
                    message.SubjectTemplate = NotificationConstants.OnboardingSubject;
                    message.ContentTemplate = NotificationConstants.NoValidGroupIdsContent;
                    await context.CallActivityAsync(nameof(SendNotification), message);
                    break;

                case nameof(NotificationMessageType.SourceNotExistNotification):
                    message.SubjectTemplate = NotificationConstants.DisabledNotificationSubject;
                    message.ContentTemplate = NotificationConstants.SyncDisabledNoGroupContent;
                    
                case nameof(NotificationMessageType.NoDataNotification):
                    message.SubjectTemplate = NotificationConstants.NoDataSubject;
                    message.ContentTemplate = NotificationConstants.NoDataContent;
                    await context.CallActivityAsync(nameof(SendNotification), message);
                    break;

                case nameof(NotificationMessageType.NoDataNotification):
                    message.SubjectTemplate = NotificationConstants.NoDataSubject;
                    message.ContentTemplate = NotificationConstants.NoDataContent;
                    await context.CallActivityAsync(nameof(SendNotification), message);
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