// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models.Helpers;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Newtonsoft.Json;
using SqlMembershipObtainer.SubOrchestrator;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Repositories.Contracts;

namespace SqlMembershipObtainer
{
    public class OrganizationProcessorFunction
    {
        public OrganizationProcessorFunction()
        {
        }

        [FunctionName(nameof(OrganizationProcessorFunction))]
        public async Task<GraphProfileInformationResponse> ProcessQueryAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
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