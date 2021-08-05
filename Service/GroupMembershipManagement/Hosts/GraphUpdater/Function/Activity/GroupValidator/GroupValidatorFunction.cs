// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Polly;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class GroupValidatorFunction
    {
        private const int NumberOfGraphRetries = 5;
        private const string SyncDisabledNoGroupEmailBody = "SyncDisabledNoGroupEmailBody";
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGraphUpdaterService _graphUpdaterService;

        public GroupValidatorFunction(ILoggingRepository loggingRepository, IGraphUpdaterService graphUpdaterService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
        }

        [FunctionName(nameof(GroupValidatorFunction))]
        public async Task<bool> ValidateGroupAsync([ActivityTrigger] GroupValidatorRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupValidatorFunction)} function started", RunId = request.RunId });

            bool isExistingGroup = false;
            var groupExistsResult = await _graphUpdaterService.GroupExistsAsync(request.GroupId, request.RunId);

            if (groupExistsResult.Outcome == OutcomeType.Successful && groupExistsResult.Result)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Group with ID {request.GroupId} exists." });
                isExistingGroup = true;
            }
            else
            {
                if (groupExistsResult.Outcome == OutcomeType.Successful)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Group with ID {request.GroupId} doesn't exist." });

                    var syncJob = await _graphUpdaterService.GetSyncJobAsync(request.JobPartitionKey, request.JobRowKey);
                    if (syncJob != null)
                        await _graphUpdaterService.SendEmailAsync(syncJob.Requestor, SyncDisabledNoGroupEmailBody, new[] { request.GroupId.ToString() }, request.RunId);
                }
                else if (groupExistsResult.FaultType == FaultType.ExceptionHandledByThisPolicy)
                    await _loggingRepository.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Exceeded {NumberOfGraphRetries} while trying to determine if a group exists." });

                if (groupExistsResult.FinalException != null)
                    throw groupExistsResult.FinalException;

                isExistingGroup = false;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupValidatorFunction)} function completed", RunId = request.RunId });
            return isExistingGroup;
        }
    }
}