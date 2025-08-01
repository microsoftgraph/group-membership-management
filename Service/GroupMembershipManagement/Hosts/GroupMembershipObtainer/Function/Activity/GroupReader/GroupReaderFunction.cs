// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class GroupReaderFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly SGMembershipCalculator _membershipCalculator;

        public GroupReaderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator membershipCalculator)
        {
            _loggingRepository = loggingRepository;
            _membershipCalculator = membershipCalculator;
        }

        [Function(nameof(GroupReaderFunction))]
        public async Task<GroupReaderResponse> GetGroupAsync([ActivityTrigger] GroupReaderRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            if (request.IsDestinationPart)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = request.RunId,
                    Message = $"Getting destination group for Part# {request.CurrentPart}, with group id {request.GroupId}."
                });
            }
            else
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = request.RunId,
                    Message = $"Getting source group for Part# {request.CurrentPart} {request.SyncJob.Query} to be synced into the destination group {request.GroupId}."
                });
            }

            var response = new GroupReaderResponse();

            if (request.IsDestinationPart)
            {
                response.SourceGroup = new AzureADGroup { ObjectId = request.GroupId };
                response.SourceGroupId = Guid.Empty.ToString();

            }
            else
            {
                response = GetSourceGroup(request);
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return response;
        }

        public GroupReaderResponse GetSourceGroup(GroupReaderRequest request)
        {
            var queryParts = JsonNode.Parse(request.SyncJob.Query).AsArray();
            var currentPart = queryParts[request.CurrentPart - 1];
            var currentQuery = currentPart.AsObject()["source"];
            var id = Convert.ToString(currentQuery);
            Guid.TryParse(id, out var parsed);
            return new GroupReaderResponse
            {
                SourceGroup = new AzureADGroup { ObjectId = parsed },
                SourceGroupId = id
            };
        }
    }
}
