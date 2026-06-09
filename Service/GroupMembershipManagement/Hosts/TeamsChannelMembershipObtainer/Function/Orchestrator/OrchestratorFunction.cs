// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using Models.Entities;
using Repositories.Contracts.Helpers;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TeamsChannelMembershipObtainer.Service.Contracts;

namespace Hosts.TeamsChannelMembershipObtainer
{
    public class OrchestratorFunction
    {
        private readonly bool _isTeamsChannelDryRunEnabled;

        public OrchestratorFunction(IDryRunValue dryRun)
        {
            _isTeamsChannelDryRunEnabled = dryRun?.DryRunEnabled ?? throw new ArgumentNullException(nameof(dryRun));
        }

        [Function(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var channelSyncInfo = context.GetInput<ChannelSyncInfo>();
            if (channelSyncInfo == null)
                throw new ArgumentNullException(nameof(channelSyncInfo), "ChannelSyncInfo cannot be null.");

            var logger = context.CreateReplaySafeLogger("TeamsChannelMembershipObtainer.OrchestratorFunction");
            var runId = channelSyncInfo.SyncJob.RunId.GetValueOrDefault(Guid.Empty);

            using var scope = logger.BeginSyncJobScope(channelSyncInfo.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = channelSyncInfo.CurrentPart,
                ["TotalParts"] = channelSyncInfo.TotalParts
            });

            try
            {
                if (channelSyncInfo.CurrentPart <= 0 || channelSyncInfo.TotalParts <= 0)
                {
                    logger.InvalidPartValues();
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = channelSyncInfo.SyncJob, Status = SyncStatus.Error });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = runId });
                    return;
                }

                logger.OrchestratorStarted(nameof(OrchestratorFunction), context.CurrentUtcDateTime);

                var parsedAndValidated = await context.CallActivityAsync<ValidateChannelResponse>(nameof(ChannelValidatorFunction), channelSyncInfo);

                if (!parsedAndValidated.IsValid)
                {
                    logger.ChannelValidationFailed(channelSyncInfo.SyncJob.Status);
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = runId });
                    return;
                }

                var users = await context.CallActivityAsync<List<AzureADTeamsUser>>(nameof(UserReaderFunction),
                    new UserReaderRequest
                    {
                        Channel = parsedAndValidated.ParsedChannel,
                        RunId = runId,
                        ChannelSyncInfo = channelSyncInfo
                    });

                var filePath = await context.CallActivityAsync<string>(nameof(FileUploaderFunction),
                     new FileUploaderRequest
                     {
                         Users = users,
                         ChannelSyncInfo = channelSyncInfo,
                         IsDryRunEnabled = _isTeamsChannelDryRunEnabled,
                         Channel = parsedAndValidated.ParsedChannel
                     });

                await context.CallActivityAsync(nameof(QueueMessageSenderFunction),
                     new QueueMessageSenderRequest
                     {
                         ChannelSyncInfo = channelSyncInfo,
                         FilePath = filePath
                     });
            }
            catch (Exception ex)
            {
                logger.UnexpectedExceptionCaught(ex);
                var syncJob = channelSyncInfo?.SyncJob;
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = syncJob?.RunId ?? Guid.Empty });
                throw;
            }

            logger.OrchestratorCompleted(nameof(OrchestratorFunction), context.CurrentUtcDateTime);
        }
    }
}