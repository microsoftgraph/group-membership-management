// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Models.Entities;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TeamsChannelMembershipObtainer.Service.Contracts;

namespace Hosts.TeamsChannelMembershipObtainer
{
    public class OrchestratorFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ITeamsChannelService _teamsChannelService;
        private readonly bool _isTeamsChannelDryRunEnabled;

        public OrchestratorFunction(ILoggingRepository loggingRepository, ITeamsChannelService teamsChannelService, IDryRunValue dryRun)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _teamsChannelService = teamsChannelService ?? throw new ArgumentNullException(nameof(teamsChannelService));
            _isTeamsChannelDryRunEnabled = dryRun?.DryRunEnabled ?? throw new ArgumentNullException(nameof(dryRun));
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context, ExecutionContext executionContext)
        {

            var channelSyncInfo = context.GetInput<ChannelSyncInfo>();
            var runId = channelSyncInfo.SyncJob.RunId.GetValueOrDefault(Guid.Empty);

            _loggingRepository.SetSyncJobProperties(runId, channelSyncInfo.SyncJob.ToDictionary());
          
            try
            {
                if (channelSyncInfo.CurrentPart <= 0 || channelSyncInfo.TotalParts <= 0)
                {
                    await context.CallActivityAsync(nameof(LoggerFunction),
                       new LoggerRequest
                       {
                           RunId = runId,
                           Message = $"Found invalid value for CurrentPart or TotalParts. Marked as Error.",
                           Verbosity = VerbosityLevel.DEBUG
                       });

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = channelSyncInfo.SyncJob, Status = SyncStatus.Error });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = runId });
                    return;
                }

                await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    RunId = runId,
                    Message = $"{nameof(OrchestratorFunction)} function started at: {context.CurrentUtcDateTime}",
                    Verbosity = VerbosityLevel.DEBUG
                });

                var parsedAndValidated = await context.CallActivityAsync<(AzureADTeamsChannel parsedChannel, bool isValid)>(nameof(ChannelValidatorFunction), channelSyncInfo);

                if (!parsedAndValidated.isValid)
                {
                    await context.CallActivityAsync(nameof(LoggerFunction),
                       new LoggerRequest
                       {
                           RunId = runId,
                           Message = $"Teams Channel Destination did not validate. Marked as {channelSyncInfo.SyncJob.Status}.",
                           Verbosity = VerbosityLevel.DEBUG
                       });

                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = runId });
                    return;
                }


                var users = await context.CallActivityAsync<List<AzureADTeamsUser>>(nameof(UserReaderFunction), 
                    new UserReaderRequest
                    {
                        Channel = parsedAndValidated.parsedChannel,
                        RunId = runId,
                        ChannelSyncInfo = channelSyncInfo
                    });


                var filePath = await context.CallActivityAsync<string>(nameof(FileUploaderFunction),
                     new FileUploaderRequest
                     {
                         Users = users,
                         ChannelSyncInfo = channelSyncInfo,
                         IsDryRunEnabled = _isTeamsChannelDryRunEnabled
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
                await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    RunId = runId,
                    Message = $"Caught unexpected exception: {ex}. Marking job as errored.",
                    Verbosity = VerbosityLevel.DEBUG
                });

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = channelSyncInfo.SyncJob, Status = SyncStatus.Error });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = runId });

                // rethrow caught exception so App Insights can get it.
                throw;
            }
            finally
            {
                _loggingRepository.RemoveSyncJobProperties(runId);
            }


            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    RunId = runId,
                    Message = $"{nameof(OrchestratorFunction)} function finished at: {context.CurrentUtcDateTime}",
                    Verbosity = VerbosityLevel.DEBUG
                });
        }
    }
}