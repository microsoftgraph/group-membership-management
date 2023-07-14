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

namespace Hosts.SecurityGroup
{
    public class SourceGroupsReaderFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly SGMembershipCalculator _membershipCalculator;

        public SourceGroupsReaderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator membershipCalculator)
        {
            _loggingRepository = loggingRepository;
            _membershipCalculator = membershipCalculator;
        }

        [FunctionName(nameof(SourceGroupsReaderFunction))]
        public async Task<AzureADGroup> GetSourceGroupsAsync([ActivityTrigger] SourceGroupsReaderRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SourceGroupsReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
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

            AzureADGroup sourceGroup = request.IsDestinationPart
                                            ? sourceGroup = new AzureADGroup { ObjectId = request.SyncJob.TargetOfficeGroupId }
                                            : sourceGroup = GetSourceGroup(request);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SourceGroupsReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return sourceGroup;
        }

        private AzureADGroup GetSourceGroup(SourceGroupsReaderRequest request)
        {
            var queryParts = JArray.Parse(request.SyncJob.Query);
            var currentPart = queryParts[request.CurrentPart - 1];
            var id = currentPart.Value<string>("source");
            Guid.TryParse(id, out var parsed);
            return new AzureADGroup { ObjectId = parsed };
        }
    }
}
