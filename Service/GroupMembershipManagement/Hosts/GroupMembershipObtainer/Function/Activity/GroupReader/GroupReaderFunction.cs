// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
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

        [FunctionName(nameof(GroupReaderFunction))]
        public async Task<AzureADGroup> GetGroupAsync([ActivityTrigger] GroupReaderRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            if (request.IsDestinationPart)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = request.RunId,
                    Message = $"Getting destination group for Part# {request.CurrentPart}, with group id {request.SyncJob.TargetOfficeGroupId}."
                });
            }
            else
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = request.RunId,
                    Message = $"Getting source group for Part# {request.CurrentPart} {request.SyncJob.Query} to be synced into the destination group {request.SyncJob.TargetOfficeGroupId}."
                });
            }

            AzureADGroup azureAdGroup = request.IsDestinationPart
                                            ? azureAdGroup = new AzureADGroup { ObjectId = request.SyncJob.TargetOfficeGroupId }
                                            : azureAdGroup = GetSourceGroup(request);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return azureAdGroup;
        }

        private AzureADGroup GetSourceGroup(GroupReaderRequest request)
        {
            var queryParts = JArray.Parse(request.SyncJob.Query);
            var currentPart = queryParts[request.CurrentPart - 1];
            var id = currentPart.Value<string>("source");
            Guid.TryParse(id, out var parsed);
            return new AzureADGroup { ObjectId = parsed };
        }
    }
}
