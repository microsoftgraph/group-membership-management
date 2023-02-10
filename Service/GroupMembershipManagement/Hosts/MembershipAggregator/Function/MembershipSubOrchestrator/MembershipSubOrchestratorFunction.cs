// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.Helpers;
using Entities.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
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

            var completedDownloadTasks = await Task.WhenAll(downloadFileTasks);
            var (SourceMembership, DestinationMembership) = ExtractMembershipInformationAsync(completedDownloadTasks, state.DestinationPart);
            var deltaCalculatorRequest = new DeltaCalculatorRequest
            {
                RunId = request.SyncJob.RunId
            };

            if (SourceMembership.SourceMembers.Count >= MEMBERS_LIMIT || DestinationMembership.SourceMembers.Count >= MEMBERS_LIMIT)
            {
                var sourceFilePath = GenerateFileName(request.SyncJob, "SourceMembership");
                var sourceContent = TextCompressor.Compress(JsonConvert.SerializeObject(SourceMembership));
                var sourceRequest = new FileUploaderRequest { FilePath = sourceFilePath, Content = sourceContent, SyncJob = request.SyncJob };

                var destinationFilePath = GenerateFileName(request.SyncJob, "DestinationMembership");
                var destinationContent = TextCompressor.Compress(JsonConvert.SerializeObject(DestinationMembership));
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
                deltaCalculatorRequest.SourceGroupMembership = TextCompressor.Compress(JsonConvert.SerializeObject(SourceMembership));
                deltaCalculatorRequest.DestinationGroupMembership = TextCompressor.Compress(JsonConvert.SerializeObject(DestinationMembership));
            }

            var deltaResponse = await context.CallActivityAsync<DeltaCalculatorResponse>(nameof(DeltaCalculatorFunction), deltaCalculatorRequest);

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
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = status, ResultStatus = ResultStatus.Success, RunId = runId });
            }
            else if (deltaResponse.MembershipDeltaStatus == MembershipDeltaStatus.DryRun)
            {
                var message = $"A Dry Run Synchronization for {request.SyncJob.TargetOfficeGroupId} is now complete. " +
                              $"{deltaResponse.MembersToAddCount} users would have been added. " +
                              $"{deltaResponse.MembersToRemoveCount} users would have been removed.";

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
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Idle, ResultStatus = ResultStatus.Success, RunId = runId });
            }
            else if (deltaResponse.MembershipDeltaStatus == MembershipDeltaStatus.Error)
            {
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    SyncJob = request.SyncJob,
                                                    Status = SyncStatus.Error
                                                });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = runId });
            }

            return new MembershipSubOrchestratorResponse
            {
                MembershipDeltaStatus = deltaResponse.MembershipDeltaStatus
            };
        }

        private (GroupMembership SourceMembership, GroupMembership DestinationMembership)
                ExtractMembershipInformationAsync((string FilePath, string Content)[] allGroupMemberships, string destinationPath)
        {
            var sourceGroupsMemberships = allGroupMemberships
                                            .Where(x => x.FilePath != destinationPath)
                                            .Select(x => JsonConvert.DeserializeObject<GroupMembership>(TextCompressor.Decompress(x.Content)))
                                            .ToList();

            var sourceGroupMembership = sourceGroupsMemberships[0];
            var toInclude = sourceGroupsMemberships.Where(g => !g.Exclusionary).SelectMany(x => x.SourceMembers).ToList();
            var toExclude = sourceGroupsMemberships.Where(g => g.Exclusionary).SelectMany(x => x.SourceMembers).ToList();
            var diff = toInclude.Except(toExclude).ToList();

            var source = sourceGroupsMemberships.SelectMany(x => x.SourceMembers).ToList();
            var listGrouped = source.GroupBy(u => u.ObjectId)
                               .Select(u => new AzureADUser() { ObjectId = u.Key, SourceGroups = u.Select(y => y.SourceGroup).Distinct().ToList() })
                               .ToList();

            var objectIds = new HashSet<Guid>(diff.Select(u => u.ObjectId));
            var sourceMembers = listGrouped.Where(u => objectIds.Contains(u.ObjectId)).ToList();

            sourceGroupMembership.SourceMembers = sourceMembers;

            var destinationMembershipFile = allGroupMemberships.First(x => x.FilePath == destinationPath);
            var destinationGroupMembership = JsonConvert.DeserializeObject<GroupMembership>(TextCompressor.Decompress(destinationMembershipFile.Content));

            return (sourceGroupMembership, destinationGroupMembership);
        }

        private FileUploaderRequest CreateAggregatedFileUploaderRequest(GroupMembership membership, DeltaCalculatorResponse deltaResponse, SyncJob syncJob)
        {
            var membersToAdd = JsonConvert.DeserializeObject<ICollection<AzureADUser>>(TextCompressor.Decompress(deltaResponse.CompressedMembersToAddJSON));
            var membersToRemove = JsonConvert.DeserializeObject<ICollection<AzureADUser>>(TextCompressor.Decompress(deltaResponse.CompressedMembersToRemoveJSON));

            var newMembership = (GroupMembership)membership.Clone();
            newMembership.SourceMembers.Clear();
            newMembership.SourceMembers.AddRange(membersToAdd);
            newMembership.SourceMembers.AddRange(membersToRemove);

            var filePath = GenerateFileName(syncJob, "Aggregated");
            var content = TextCompressor.Compress(JsonConvert.SerializeObject(newMembership));

            return new FileUploaderRequest { FilePath = filePath, Content = content, SyncJob = syncJob };
        }

        private string GenerateFileName(SyncJob syncJob, string suffix)
        {
            var timeStamp = syncJob.Timestamp.GetValueOrDefault().ToString("MMddyyyy-HHmmss");
            return $"/{syncJob.TargetOfficeGroupId}/{timeStamp}_{syncJob.RunId}_{suffix}.json";
        }
    }
}
