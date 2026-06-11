// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class GroupReaderFunction
    {
        private readonly ILogger<GroupReaderFunction> _logger;
        private readonly SGMembershipCalculator _membershipCalculator;

        public GroupReaderFunction(ILogger<GroupReaderFunction> logger, SGMembershipCalculator membershipCalculator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _membershipCalculator = membershipCalculator;
        }

        [Function(nameof(GroupReaderFunction))]
        public async Task<GroupReaderResponse> GetGroupAsync([ActivityTrigger] GroupReaderRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.FunctionStarted(nameof(GroupReaderFunction));
                if (request.IsDestinationPart)
                {
                    _logger.GettingDestinationGroup(request.CurrentPart, request.GroupId);
                }
                else
                {
                    _logger.GettingSourceGroup(request.CurrentPart, request.SyncJob.Query, request.GroupId);
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

                _logger.FunctionCompleted(nameof(GroupReaderFunction));
                return response;
            }
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