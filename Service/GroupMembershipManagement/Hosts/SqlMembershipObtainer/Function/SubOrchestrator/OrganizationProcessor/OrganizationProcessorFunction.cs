// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.SqlMembershipObtainer;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using SqlMembershipObtainer.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SqlMembershipObtainer
{
    public class OrganizationProcessorFunction
    {
        [Function(nameof(OrganizationProcessorFunction))]
        public async Task<MembershipFileResult> ProcessQueryAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var response = new MembershipFileResult();
            var request = context.GetInput<OrganizationProcessorRequest>();

            var logger = context.CreateReplaySafeLogger("SqlMembershipObtainer.OrganizationProcessorFunction");
            using var scope = logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            });

            logger.FunctionStarted(nameof(OrganizationProcessorFunction));

            var tableName = await context.CallActivityAsync<string>(nameof(TableNameReaderFunction), new TableNameReaderRequest { SyncJob = request.SyncJob, GroupId = request.GroupId, CurrentPart = request.CurrentPart, TotalParts = request.TotalParts });
            if (string.IsNullOrWhiteSpace(tableName))
            {
                logger.TableDoesNotExist();
                return response;
            }

            var filter = request.Query.Filter;
            var manager = request.Query.Manager;

            if (manager != null && manager.Id > 0)
            {
                response = await context.CallActivityAsync<MembershipFileResult>(
                                                    nameof(ManagerOrgReaderFunction),
                                                    new ManagerOrgReaderRequest
                                                    {
                                                        Filter = filter,
                                                        Depth = manager.Depth,
                                                        PersonnelNumber = manager.Id,
                                                        SyncJob = request.SyncJob,
                                                        GroupId = request.GroupId,
                                                        CurrentPart = request.CurrentPart,
                                                        TotalParts = request.TotalParts,
                                                        Exclusionary = request.Exclusionary,
                                                        TableName = tableName
                                                    });
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    response = await context.CallActivityAsync<MembershipFileResult>(
                                                                nameof(ChildEntitiesFilterFunction),
                                                                new ChildEntitiesFilterRequest
                                                                {
                                                                    Query = filter,
                                                                    SyncJob = request.SyncJob,
                                                                    GroupId = request.GroupId,
                                                                    CurrentPart = request.CurrentPart,
                                                                    TotalParts = request.TotalParts,
                                                                    Exclusionary = request.Exclusionary,
                                                                    TableName = tableName
                                                                });
                }
            }

            logger.FunctionCompleted(nameof(OrganizationProcessorFunction));

            return response;
        }
    }
}