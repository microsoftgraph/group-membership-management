// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TeamsChannelMembershipObtainer.Service.Contracts;

namespace Hosts.TeamsChannelMembershipObtainer
{
    public class FileUploaderFunction
    {
        private readonly ILogger<FileUploaderFunction> _logger;
        private readonly ITeamsChannelService _teamsChannelService;

        public FileUploaderFunction(ILogger<FileUploaderFunction> logger, ITeamsChannelService teamsChannelService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _teamsChannelService = teamsChannelService ?? throw new ArgumentNullException(nameof(teamsChannelService));
        }

        [Function(nameof(FileUploaderFunction))]
        public async Task<string> UploadFileAsync([ActivityTrigger] FileUploaderRequest request)
        {
            using var scope = _logger.BeginSyncJobScope(request.ChannelSyncInfo.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.ChannelSyncInfo.CurrentPart,
                ["TotalParts"] = request.ChannelSyncInfo.TotalParts
            });

            _logger.FunctionStarted(nameof(FileUploaderFunction));
            _logger.UploadingUsers(request.Users.Count, request.Channel.ObjectId, request.Channel.ChannelId);

            var filePath = await _teamsChannelService.UploadMembershipAsync(request.Users, request.ChannelSyncInfo, request.IsDryRunEnabled, request.Channel.ObjectId);

            _logger.UploadedUsers(request.Users.Count, request.Channel.ObjectId, request.Channel.ChannelId, filePath);
            _logger.FunctionCompleted(nameof(FileUploaderFunction));

            return filePath;
        }
    }
}
