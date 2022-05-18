// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Newtonsoft.Json;
using Repositories.Contracts.InjectConfig;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class MembershipSubOrchestratorFunction
    {
        private readonly IThresholdConfig _thresholdConfig = null;

        public MembershipSubOrchestratorFunction(IThresholdConfig thresholdConfig)
        {
            _thresholdConfig = thresholdConfig ?? throw new ArgumentNullException(nameof(thresholdConfig));
        }

        [FunctionName(nameof(MembershipSubOrchestratorFunction))]
        public async Task<MembershipSubOrchestratorResponse> RunMembershipSubOrchestratorFunctionAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var request = context.GetInput<MembershipSubOrchestratorRequest>();
            var proxy = context.CreateEntityProxy<IJobTracker>(request.EntityId);
            var state = await proxy.GetState();
            var downloadFileTasks = new List<Task<(string FilePath, string Content)>>();
            var dynamicProperties = request.SyncJob.ToDictionary();

            foreach (var part in state.CompletedParts)
            {
                var downloadRequest = new FileDownloaderRequest { FilePath = part, SyncJob = request.SyncJob };
                downloadFileTasks.Add(context.CallActivityAsync<(string FilePath, string Content)>(nameof(FileDownloaderFunction), downloadRequest));
            }

            var (SourceMembership, DestinationMembership) = (await Task.WhenAll(ExtractMembershipInformationAsync(downloadFileTasks, state.DestinationPart))).First();

            var deltaResponse = await context.CallActivityAsync<DeltaResponse>(nameof(DeltaCalculatorFunction),
                                                                                new DeltaCalculatorRequest
                                                                                {
                                                                                    SourceGroupMembership = SourceMembership,
                                                                                    DestinationGroupMembership = DestinationMembership
                                                                                });

            if (deltaResponse.MembershipDeltaStatus == MembershipDeltaStatus.Ok)
            {
                var uploadRequest = CreateFileUploaderRequest(SourceMembership, deltaResponse, request.SyncJob);
                await context.CallActivityAsync(nameof(FileUploaderFunction), uploadRequest);
                await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LogMessage
                                                {
                                                    Message = $"Uploaded membership file {uploadRequest.FilePath} with {SourceMembership.SourceMembers.Count} unique members",
                                                    DynamicProperties = dynamicProperties
                                                });

                return new MembershipSubOrchestratorResponse
                {
                    FilePath = uploadRequest.FilePath,
                    MembershipDeltaStatus = deltaResponse.MembershipDeltaStatus
                };
            }
            else if (deltaResponse.MembershipDeltaStatus == MembershipDeltaStatus.ThresholdExceeded)
            {
                var currentThresholdViolations = request.SyncJob.ThresholdViolations + 1;
                SyncStatus? status = currentThresholdViolations >= _thresholdConfig.NumberOfThresholdViolationsToDisableJob
                                    ? SyncStatus.ThresholdExceeded
                                    : SyncStatus.Idle;

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    SyncJob = request.SyncJob,
                                                    Status = status,
                                                    ThresholdViolations = currentThresholdViolations
                                                });
            }
            else if (deltaResponse.MembershipDeltaStatus == MembershipDeltaStatus.DryRun)
            {
                var message = $"A Dry Run Synchronization for {request.SyncJob.TargetOfficeGroupId} is now complete. " +
                              $"{deltaResponse.MembersToAdd.Count} users would have been added. " +
                              $"{deltaResponse.MembersToRemove.Count} users would have been removed.";

                await context.CallActivityAsync(nameof(LoggerFunction), new LogMessage { Message = message, DynamicProperties = dynamicProperties });

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    SyncJob = request.SyncJob,
                                                    Status = SyncStatus.Idle,
                                                    IsDryRun = true
                                                });
            }
            else if (deltaResponse.MembershipDeltaStatus == MembershipDeltaStatus.Error)
            {
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    SyncJob = request.SyncJob,
                                                    Status = SyncStatus.Error
                                                });
            }

            return new MembershipSubOrchestratorResponse
            {
                MembershipDeltaStatus = deltaResponse.MembershipDeltaStatus
            };
        }

        private async Task<(GroupMembership SourceMembership, GroupMembership DestinationMembership)>
                ExtractMembershipInformationAsync(List<Task<(string FilePath, string Content)>> membershipTasks, string destinationPath)
        {
            var allGroupMemberships = await Task.WhenAll(membershipTasks);
            var sourceGroupsMemberships = allGroupMemberships
                                            .Where(x => x.FilePath != destinationPath)
                                            .Select(x => JsonConvert.DeserializeObject<GroupMembership>(x.Content))
                                            .ToList();

            var sourceGroupMembership = sourceGroupsMemberships[0];
            sourceGroupMembership.SourceMembers = sourceGroupsMemberships.SelectMany(x => x.SourceMembers).Distinct().ToList();

            var destinationGroupMembership = JsonConvert.DeserializeObject<GroupMembership>(allGroupMemberships.First(x => x.FilePath == destinationPath).Content);

            return (sourceGroupMembership, destinationGroupMembership);
        }

        private FileUploaderRequest CreateFileUploaderRequest(GroupMembership membership, DeltaResponse deltaResponse, SyncJob syncJob)
        {
            var newMembership = (GroupMembership)membership.Clone();
            newMembership.SourceMembers.Clear();
            newMembership.SourceMembers.AddRange(deltaResponse.MembersToAdd);
            newMembership.SourceMembers.AddRange(deltaResponse.MembersToRemove);

            var timeStamp = syncJob.Timestamp.ToString("MMddyyyy-HHmmss");
            var filePath = $"/{newMembership.Destination.ObjectId}/{timeStamp}_{syncJob.RunId}_Aggregated.json";
            var content = JsonConvert.SerializeObject(newMembership);

            return new FileUploaderRequest { FilePath = filePath, Content = content, SyncJob = syncJob };
        }
    }
}
