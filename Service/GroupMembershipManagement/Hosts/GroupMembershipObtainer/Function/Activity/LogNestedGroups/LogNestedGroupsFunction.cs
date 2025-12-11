// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class LogNestedGroupsFunction
    {
        private readonly ILoggingRepository _log;
        private readonly IGraphGroupRepository _graphGroupRepository;

        public LogNestedGroupsFunction(ILoggingRepository loggingRepository, IGraphGroupRepository graphGroupRepository)
        {
            _log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        [Function(nameof(LogNestedGroupsFunction))]
        public async Task LogNestedGroupsAsync([ActivityTrigger] LogNestedGroupsRequest request)
        {
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(LogNestedGroupsFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            try
            {
                var groups = await _graphGroupRepository.GetDirectGroupTypeMembersAsync(request.GroupId);

                await _log.LogMessageAsync(new LogMessage
                {
                    RunId = request.RunId,
                    Message = $"Retrieved {groups.Count} group-type members for group {request.GroupId}. Group IDs: {string.Join(", ", groups.Select(g => g.ObjectId))}"
                });
            }
            catch (Exception ex)
            {
                await _log.LogMessageAsync(new LogMessage
                {
                    RunId = request.RunId,
                    Message = $"Error retrieving group-type members for group {request.GroupId}: {ex.Message}"
                });
                throw;
            }

            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(LogNestedGroupsFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
        }
    }
}
