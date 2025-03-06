// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;
using TeamsChannelMembershipObtainer.Service.Contracts;

namespace Hosts.TeamsChannelMembershipObtainer
{
    public class FileUploaderFunction
    {
        private readonly ITeamsChannelService _teamsChannelService;
        private readonly ILoggingRepository _loggingRepository;

        public FileUploaderFunction(ILoggingRepository loggingRepository, ITeamsChannelService teamsChannelService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _teamsChannelService = teamsChannelService ?? throw new ArgumentNullException(nameof(teamsChannelService));
        }

        [FunctionName(nameof(FileUploaderFunction))]
        public async Task<string> UploadFileAsync([ActivityTrigger] FileUploaderRequest request)
        {
            var runId = request.ChannelSyncInfo.SyncJob.RunId.GetValueOrDefault(Guid.Empty);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(FileUploaderFunction)} function started", RunId = runId }, VerbosityLevel.DEBUG);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Uploading {request.Users.Count} users from {request.ChannelSyncInfo.SyncJob.Destination} to blob storage.", RunId = runId });

            var filePath = await _teamsChannelService.UploadMembershipAsync(request.Users, request.ChannelSyncInfo, request.IsDryRunEnabled);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Uploaded {request.Users.Count} users from {request.ChannelSyncInfo.SyncJob.Destination} to blob storage at {filePath}.", RunId = runId });
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(FileUploaderFunction)} function completed", RunId = runId }, VerbosityLevel.DEBUG);

            return filePath;
        }
    }
}
