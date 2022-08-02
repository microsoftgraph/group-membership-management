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
        private const int MEMBERS_LIMIT = 100000;
        private readonly IThresholdConfig _thresholdConfig = null;

        public MembershipSubOrchestratorFunction(IThresholdConfig thresholdConfig)
        {
            _thresholdConfig = thresholdConfig ?? throw new ArgumentNullException(nameof(thresholdConfig));
        }

        [FunctionName(nameof(MembershipSubOrchestratorFunction))]
        public async Task<MembershipSubOrchestratorResponse> RunMembershipSubOrchestratorFunctionAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var request = context.GetInput<MembershipSubOrchestratorRequest>();
            var runId = request.SyncJob.RunId.GetValueOrDefault(Guid.Empty);
            var proxy = context.CreateEntityProxy<IJobTracker>(request.EntityId);
            var state = await proxy.GetState();
            var downloadFileTasks = new List<Task<(string FilePath, string Content)>>();

            foreach (var part in state.CompletedParts)
            {
                var downloadRequest = new FileDownloaderRequest { FilePath = part, SyncJob = request.SyncJob };
                downloadFileTasks.Add(context.CallActivityAsync<(string FilePath, string Content)>(nameof(FileDownloaderFunction), downloadRequest));
            }

            var (SourceMembership, DestinationMembership) = (await Task.WhenAll(ExtractMembershipInformationAsync(downloadFileTasks, state.DestinationPart))).First();
            var deltaCalculatorRequest = new DeltaCalculatorRequest
            {
                RunId = request.SyncJob.RunId
            };

            if (SourceMembership.SourceMembers.Count >= MEMBERS_LIMIT || DestinationMembership.SourceMembers.Count >= MEMBERS_LIMIT)
            {
                var sourceFilePath = GenerateFileName(request.SyncJob, "SourceMembership");
                var sourceContent = JsonConvert.SerializeObject(SourceMembership);
                var sourceRequest = new FileUploaderRequest { FilePath = sourceFilePath, Content = sourceContent, SyncJob = request.SyncJob };

                var destinationFilePath = GenerateFileName(request.SyncJob, "DestinationMembership");
                var destinationContent = JsonConvert.SerializeObject(DestinationMembership);
                var destinationRequest = new FileUploaderRequest { FilePath = destinationFilePath, Content = destinationContent, SyncJob = request.SyncJob };

                await Task.WhenAll
                (
                    context.CallActivityAsync(nameof(FileUploaderFunction), sourceRequest),
                    context.CallActivityAsync(nameof(FileUploaderFunction), destinationRequest)
                );

                deltaCalculatorRequest.ReadFromBlobs = true;
                deltaCalculatorRequest.SourceMembershipFilePath = sourceFilePath;
                deltaCalculatorRequest.DestinationMembershipFilePath = destinationFilePath;
            }
            else
            {
                deltaCalculatorRequest.SourceGroupMembership = SourceMembership;
                deltaCalculatorRequest.DestinationGroupMembership = DestinationMembership;
            }

            var deltaResponse = await context.CallActivityAsync<DeltaResponse>(nameof(DeltaCalculatorFunction), deltaCalculatorRequest);

            if (deltaResponse.MembershipDeltaStatus == MembershipDeltaStatus.Ok)
            {
                var uploadRequest = CreateAggregatedFileUploaderRequest(SourceMembership, deltaResponse, request.SyncJob);
                await context.CallActivityAsync(nameof(FileUploaderFunction), uploadRequest);
                await context.CallActivityAsync(nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        Message = new LogMessage
                        {
                            Message = $"Uploaded membership file {uploadRequest.FilePath} with {SourceMembership.SourceMembers.Count} unique members",
                            RunId = runId
                        }
                    });

                return new MembershipSubOrchestratorResponse
                {
                    FilePath = uploadRequest.FilePath,
                    MembershipDeltaStatus = deltaResponse.MembershipDeltaStatus,
                    ProjectedMemberCount = SourceMembership.SourceMembers.Count
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

                await context.CallActivityAsync(nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        Message = new LogMessage { Message = message, RunId = runId }
                    });
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
            var toInclude = sourceGroupsMemberships.Where(g => g.Exclusionary == true).SelectMany(x => x.SourceMembers).Distinct().ToList();
            var toExclude = sourceGroupsMemberships.Where(g => g.Exclusionary == false).SelectMany(x => x.SourceMembers).Distinct().ToList();
            sourceGroupMembership.SourceMembers = toInclude.Except(toExclude).ToList();

            var destinationGroupMembership = JsonConvert.DeserializeObject<GroupMembership>(allGroupMemberships.First(x => x.FilePath == destinationPath).Content);

            return (sourceGroupMembership, destinationGroupMembership);
        }

        private FileUploaderRequest CreateAggregatedFileUploaderRequest(GroupMembership membership, DeltaResponse deltaResponse, SyncJob syncJob)
        {
            var newMembership = (GroupMembership)membership.Clone();
            newMembership.SourceMembers.Clear();
            newMembership.SourceMembers.AddRange(deltaResponse.MembersToAdd);
            newMembership.SourceMembers.AddRange(deltaResponse.MembersToRemove);

            var filePath = GenerateFileName(syncJob, "Aggregated");
            var content = JsonConvert.SerializeObject(newMembership);

            return new FileUploaderRequest { FilePath = filePath, Content = content, SyncJob = syncJob };
        }

        private string GenerateFileName(SyncJob syncJob, string suffix)
        {
            var timeStamp = syncJob.Timestamp.GetValueOrDefault().ToString("MMddyyyy-HHmmss");
            return $"/{syncJob.TargetOfficeGroupId}/{timeStamp}_{syncJob.RunId}_{suffix}.json";
        }
    }
}
