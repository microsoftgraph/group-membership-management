// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Models;
using Models.Helpers;
using Newtonsoft.Json;
using Repositories.Contracts;
using SqlMembershipObtainer.SubOrchestrator;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SqlMembershipObtainer
{
    public class OrganizationProcessorFunction
    {
        public OrganizationProcessorFunction()
        {
        }

        [Function(nameof(OrganizationProcessorFunction))]
        public async Task<GraphProfileInformationResponse> ProcessQueryAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            List<GraphProfileInformation> graphProfileInformation = null;
            var response = new GraphProfileInformationResponse();
            var queryTasks = new List<Task<GraphProfileInformationResponse>>();
            var request = context.GetInput<OrganizationProcessorRequest>();

            await context.CallActivityAsync(
                            nameof(LoggerFunction),
                            new LoggerRequest
                            {
                                SyncJob = request.SyncJob,
                                Message = $"{nameof(OrganizationProcessorFunction)} function started",
                                Verbosity = VerbosityLevel.DEBUG
                            });

            var tableName = await context.CallActivityAsync<string>(nameof(TableNameReaderFunction), request.SyncJob);
            if (string.IsNullOrWhiteSpace(tableName))
            {
                await context.CallActivityAsync(
                            nameof(LoggerFunction),
                            new LoggerRequest
                            {
                                SyncJob = request.SyncJob,
                                Message = "Table does not exist",
                            });
                return response;
            }

            var filter = request.Query.Filter;
            var manager = request.Query.Manager;

            if (manager != null && manager.Id > 0)
            {
                var res = await context.CallActivityAsync<GraphProfileInformationResponse>(
                                                    nameof(ManagerOrgReaderFunction),
                                                    new ManagerOrgReaderRequest
                                                    {
                                                        Filter = filter,
                                                        Depth = manager.Depth,
                                                        PersonnelNumber = manager.Id,
                                                        SyncJob = request.SyncJob,
                                                        TableName = tableName
                                                    });

                graphProfileInformation = JsonConvert.DeserializeObject<List<GraphProfileInformation>>(TextCompressor.Decompress(res.GraphProfiles));
                graphProfileInformation = graphProfileInformation.GroupBy(user => user.Id).Select(userGrp => userGrp.First()).ToList();
                response = new GraphProfileInformationResponse
                {
                    GraphProfiles = TextCompressor.Compress(JsonConvert.SerializeObject(graphProfileInformation)),
                    GraphProfileCount = res.GraphProfileCount
                };
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    response = await context.CallActivityAsync<GraphProfileInformationResponse>(
                                                                nameof(ChildEntitiesFilterFunction),
                                                                new ChildEntitiesFilterRequest
                                                                {
                                                                    Query = filter,
                                                                    SyncJob = request.SyncJob,
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