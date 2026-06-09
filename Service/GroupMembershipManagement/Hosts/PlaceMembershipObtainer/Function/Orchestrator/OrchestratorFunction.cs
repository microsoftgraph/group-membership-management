// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Hosts.PlaceMembershipObtainer
{
    public class OrchestratorFunction
    {
        private readonly IConfiguration _configuration;
        private readonly PlaceMembershipObtainerService _calculator;

        public OrchestratorFunction(
            PlaceMembershipObtainerService calculator,
            IConfiguration configuration)
        {
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        [Function(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var logger = context.CreateReplaySafeLogger("PlaceMembershipObtainer.OrchestratorFunction");
            var mainRequest = context.GetInput<OrchestratorRequest>();
            var syncJob = mainRequest.SyncJob;
            var runId = syncJob.RunId.GetValueOrDefault(Guid.Empty);
            List<AzureADUser> distinctUsers = null;

            using (logger.BeginSyncJobScope(syncJob))
            {
                logger.FunctionStarted(nameof(OrchestratorFunction));

                try
                {
                    var groupId = await context.CallActivityAsync<Guid>(nameof(GetGroupFunction), syncJob);
                    if (groupId.Equals(Guid.Empty))
                    {
                        logger.UnableToGetGroupId(syncJob.Id);
                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.Error, SyncJob = syncJob });
                        return;
                    }
                    logger.GroupIdFound(syncJob.Id, groupId);
                    var queryParts = JsonNode.Parse(syncJob.Query).AsArray();
                    if (mainRequest.CurrentPart == mainRequest.TotalParts)
                    {
                        logger.TargetGroup();
                        return;
                    }

                    var currentPart = queryParts[mainRequest.CurrentPart - 1];
                    var currentType = currentPart["type"].GetValue<string>();

                    if (currentType != "PlaceMembership")
                    {
                        logger.NotPlaceMembershipType();
                        return;
                    }

                    var currentQuery = currentPart["source"].GetValue<string>();
                    var currentQueryAsString = Convert.ToString(currentQuery);

                    if (string.IsNullOrWhiteSpace(currentQueryAsString))
                    {
                        logger.NoUrlFoundInPart(mainRequest.CurrentPart, syncJob.Query);
                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });
                        return;
                    }

                    else
                    {
                        try
                        {
                            var hasValidJson = await context.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), new SchemaValidatorRequest { Query = currentPart.ToString(), RunId = syncJob.RunId });
                            if (!hasValidJson)
                            {
                                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.SchemaError, SyncJob = syncJob });
                                return;
                            }
                        }
                        catch (JsonException)
                        {
                            logger.SourceQueryNotValidForJob(syncJob.Id);
                            await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.QueryNotValid, SyncJob = syncJob });
                            return;
                        }
                    }

                    var response = await context.CallSubOrchestratorAsync<SubOrchestratorResponse>(nameof(SubOrchestratorFunction),
                        new SubOrchestratorRequest { SyncJob = syncJob, Url = currentQueryAsString, RunId = runId });

                    if (response.Status != SyncStatus.InProgress)
                    {
                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });
                        return;
                    }

                    var users = response.Users;
                    distinctUsers = users.GroupBy(user => user.ObjectId).Select(userGrp => userGrp.First()).ToList();

                    logger.FoundDuplicateUsers(users.Count - distinctUsers.Count, distinctUsers.Count, syncJob.Query, groupId);

                    var filePath = await context.CallActivityAsync<string>(
                                        nameof(UsersSenderFunction),
                                        new UsersSenderRequest
                                        {
                                            SyncJob = syncJob,
                                            RunId = runId,
                                            GroupId = groupId,
                                            Users = distinctUsers,
                                            CurrentPart = mainRequest.CurrentPart,
                                            Exclusionary = mainRequest.Exclusionary
                                        });

                    if (!string.IsNullOrWhiteSpace(filePath))
                    {
                        logger.CallingMembershipAggregator();
                        var content = new MembershipAggregatorHttpRequest
                        {
                            FilePath = filePath,
                            PartNumber = mainRequest.CurrentPart,
                            PartsCount = mainRequest.TotalParts,
                            SyncJob = mainRequest.SyncJob,
                            IsDestinationPart = false
                        };

                        await context.CallActivityAsync(nameof(QueueMessageSenderFunction), content);
                    }
                    else
                    {
                        logger.MembershipFilePathNotValid(SyncStatus.FilePathNotValid.ToString());

                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.FilePathNotValid });
                    }

                }
                catch (Exception ex)
                {
                    if (ex.Message != null && ex.Message.Contains("The request timed out"))
                    {
                        syncJob.StartDate = context.CurrentUtcDateTime.AddMinutes(30);
                        logger.ReschedulingJobDueToTimeout(syncJob.StartDate);
                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Idle });
                        return;
                    }

                    logger.UnexpectedExceptionInPart(mainRequest.CurrentPart, ex);

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });

                    // make sure this gets thrown to where App Insights will handle it
                    throw;
                }

                logger.FunctionCompleted(nameof(OrchestratorFunction));
            }
        }
    }
}