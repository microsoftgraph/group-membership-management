// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class GroupUpdaterService : IGroupUpdaterService
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly bool _isDryRunEnabled;

        public GroupUpdaterService(
            ILoggingRepository loggingRepository,
            IGraphGroupRepository graphGroupRepository,
            IDryRunValue dryRun
            )
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _isDryRunEnabled = _loggingRepository.DryRun = dryRun != null ? dryRun.DryRunEnabled : throw new ArgumentNullException(nameof(dryRun));
        }

        public async Task<GraphUpdaterStatus> AddUsersToGroupAsync(ICollection<AzureADUser> members, Guid targetGroupId, Guid runId)
        {
            if (_isDryRunEnabled)
            {
                return GraphUpdaterStatus.Ok;
            }

            var stopwatch = Stopwatch.StartNew();
            var graphResponse = await _graphGroupRepository.AddUsersToGroup(members, new AzureADGroup { ObjectId = targetGroupId });
            stopwatch.Stop();

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Adding {members.Count} users to group {targetGroupId} complete in {stopwatch.Elapsed.TotalSeconds} seconds. " +
                $"{members.Count / stopwatch.Elapsed.TotalSeconds} users added per second. ",
                RunId = runId
            });

            return graphResponse == ResponseCode.Error ? GraphUpdaterStatus.Error : GraphUpdaterStatus.Ok;
        }

        public async Task<GraphUpdaterStatus> RemoveUsersFromGroupAsync(ICollection<AzureADUser> members, Guid targetGroupId, Guid runId)
        {
            if (_isDryRunEnabled)
            {
                return GraphUpdaterStatus.Ok;
            }

            var stopwatch = Stopwatch.StartNew();
            var graphResponse = await _graphGroupRepository.RemoveUsersFromGroup(members, new AzureADGroup { ObjectId = targetGroupId });
            stopwatch.Stop();

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Removing {members.Count} users from group {targetGroupId} complete in {stopwatch.Elapsed.TotalSeconds} seconds. " +
                $"{members.Count / stopwatch.Elapsed.TotalSeconds} users removed per second.",
                RunId = runId
            });

            return graphResponse == ResponseCode.Error ? GraphUpdaterStatus.Error : GraphUpdaterStatus.Ok;
        }
    }
}