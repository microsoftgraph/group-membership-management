// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Repositories.Contracts;
using SqlMembershipObtainer.Entities;
using System.Threading.Tasks;

namespace SqlMembershipObtainer
{
    public class OrganizationProcessorFunction
    {
        public OrganizationProcessorFunction()
        {
        }

        [Function(nameof(OrganizationProcessorFunction))]
        public async Task<MembershipFileResult> ProcessQueryAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var response = new MembershipFileResult();           
            var request = context.GetInput<OrganizationProcessorRequest>();

            await context.CallActivityAsync(
                            nameof(LoggerFunction),
                            new LoggerRequest
                            {
                                SyncJob = request.SyncJob,
                                Message = $"{nameof(OrganizationProcessorFunction)} function started",
                                Verbosity = VerbosityLevel.DEBUG
                            });

            var tableName = await context.CallActivityAsync<string>(nameof(TableNameReaderFunction), new TableNameReaderRequest { SyncJob = request.SyncJob, GroupId = request.GroupId });
            if (string.IsNullOrWhiteSpace(tableName))
            {
                await context.CallActivityAsync(
                            nameof(LoggerFunction),
                            new LoggerRequest
                            {
                                SyncJob = request.SyncJob,
                                Message = "Table does not exist",
                                Verbosity = VerbosityLevel.INFO
                            });
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
                                                                    Exclusionary = request.Exclusionary,
                                                                    TableName = tableName
                                                                });
                }
            }

            await context.CallActivityAsync(
                           nameof(LoggerFunction),
                           new LoggerRequest
                           {
                               SyncJob = request.SyncJob,
                               Message = $"{nameof(OrganizationProcessorFunction)} function completed",
                               Verbosity = VerbosityLevel.DEBUG
                           });

            return response;
        }
    }
}