// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System.Collections.Generic;
using SqlMembershipObtainer.SubOrchestrator;
using System.Threading.Tasks;
using Repositories.Contracts;

namespace SqlMembershipObtainer
{
    public class ManagerOrgProcessorFunction
    {
        public ManagerOrgProcessorFunction()
        {
        }

        [FunctionName(nameof(ManagerOrgProcessorFunction))]
        public async Task<GraphProfileInformationResponse> ProcessQueryAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            GraphProfileInformationResponse response = null;
            var request = context.GetInput<ManagerOrgProcessorRequest>();

            await context.CallActivityAsync(
                            nameof(LoggerFunction),
                            new LoggerRequest
                            {
                                SyncJob = request.SyncJob,
                                Message = $"{nameof(ManagerOrgProcessorFunction)} function started",
                                Verbosity = VerbosityLevel.DEBUG
                            });

            response = await context.CallActivityAsync<GraphProfileInformationResponse>(
                                                    nameof(ManagerOrgReaderFunction),
                                                    new ManagerOrgReaderRequest
                                                    {
                                                        Filter = request.Filter,
                                                        Depth = request.Depth,
                                                        PersonnelNumber = request.PersonnelNumber,
                                                        SyncJob = request.SyncJob,
                                                        TableName = request.TableName
                                                    });

            await context.CallActivityAsync(
                           nameof(LoggerFunction),
                           new LoggerRequest
                           {
                               SyncJob = request.SyncJob,
                               Message = $"{nameof(ManagerOrgProcessorFunction)} function completed",
                               Verbosity = VerbosityLevel.DEBUG
                           });

            return response;
        }
    }
}