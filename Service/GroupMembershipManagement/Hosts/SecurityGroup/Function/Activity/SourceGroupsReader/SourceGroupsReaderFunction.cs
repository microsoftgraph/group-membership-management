// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using System.Threading.Tasks;

namespace Hosts.SecurityGroup
{
    public class SourceGroupsReaderFunction
    {
        private readonly ILoggingRepository _log;
        private readonly SGMembershipCalculator _calculator;

        public SourceGroupsReaderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
        {
            _log = loggingRepository;
            _calculator = calculator;
        }

        [FunctionName(nameof(SourceGroupsReaderFunction))]
        public async Task<AzureADGroup[]> GetSourceGroupsAsync([ActivityTrigger] SourceGroupsReaderRequest request)
        {
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SourceGroupsReaderFunction)} function started", RunId = request.RunId });
            await _log.LogMessageAsync(new LogMessage
            {
                RunId = request.RunId,
                Message = $"Reading source groups for Part# {request.CurrentPart} {request.SyncJob.Query} to be synced into the destination group {request.SyncJob.TargetOfficeGroupId}."
            });

            var queryParts = JArray.Parse(request.SyncJob.Query);
            var currentPart = queryParts[request.CurrentPart - 1];
            var ids = string.Join(";", currentPart.Value<JArray>("sources"));
            var sourceGroups = _calculator.ReadSourceGroups(ids);

            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SourceGroupsReaderFunction)} function completed", RunId = request.RunId });
            return sourceGroups;
        }
    }
}
