// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using MembershipAggregator.Helpers;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Newtonsoft.Json;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class MembershipSubOrchestratorFunction
    {
        private const string NoDataEmailSubject = "NoDataEmailSubject";
        private const string NoDataEmailContent = "NoDataEmailContent";
        private const int MEMBERS_LIMIT = 100000;
        private readonly IThresholdConfig _thresholdConfig = null;
        private readonly IGraphAPIService _graphAPIService = null;
        private readonly TelemetryClient _telemetryClient = null;

        public MembershipSubOrchestratorFunction(IThresholdConfig thresholdConfig, IGraphAPIService graphAPIService, TelemetryClient telemetryClient)
        {
            _thresholdConfig = thresholdConfig ?? throw new ArgumentNullException(nameof(thresholdConfig));
            _graphAPIService = graphAPIService ?? throw new ArgumentNullException(nameof(graphAPIService));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
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

            if (!request.SyncJob.AllowEmptyDestination && SourceMembership.SourceMembers.Count == 0)
            {
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                                new JobStatusUpdaterRequest
                                                {
                                                    SyncJob = request.SyncJob,
                                                    Status = SyncStatus.MembershipDataNotFound
                                                });
                await context.CallActivityAsync(nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        Message = new LogMessage
                        {
                            Message = $"Sources are empty for TargetOfficeGroupId {request.SyncJob.TargetOfficeGroupId}. Empty destination is not allowed for this group. Marking job as 'MembershipDataNotFound'.",
                            RunId = runId
                        }
                    });


                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest
                {
                    JobStatus = SyncStatus.MembershipDataNotFound,
                    ResultStatus = ResultStatus.Success,
                    RunId = runId
                });

                var groupInformation = await context.CallActivityAsync<SyncJobGroup>(nameof(GroupNameReaderFunction), request.SyncJob);

                await _graphAPIService.SendEmailAsync(
                    toEmail: request.SyncJob.Requestor,
                    contentTemplate: NoDataEmailContent,
                    additionalContentParams: new[] { request.SyncJob.TargetOfficeGroupId.ToString(), groupInformation.Name },
                    runId,
                    emailSubject: NoDataEmailSubject,
                    additionalSubjectParams: new[] { request.SyncJob.TargetOfficeGroupId.ToString(), groupInformation.Name });

                return new MembershipSubOrchestratorResponse
                {
                    MembershipDeltaStatus = MembershipDeltaStatus.Error

                };
            }

            if (SourceMembership.SourceMembers.Count >= MEMBERS_LIMIT || DestinationMembership.SourceMembers.Count >= MEMBERS_LIMIT)
            {
                var sourceFilePath = GenerateFileName(request.SyncJob, "SourceMembership", context);
                var sourceContent = TextCompressor.Compress(JsonConvert.SerializeObject(SourceMembership));
                var sourceRequest = new FileUploaderRequest { FilePath = sourceFilePath, Content = sourceContent, SyncJob = request.SyncJob };

                var destinationFilePath = GenerateFileName(request.SyncJob, "DestinationMembership", context);
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
                var uploadRequest = CreateAggregatedFileUploaderRequest(SourceMembership, deltaResponse, request.SyncJob, context);
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
                var uploadRequest = CreateAggregatedFileUploaderRequest(SourceMembership, deltaResponse, request.SyncJob, context);
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
            else if (deltaResponse.MembershipDeltaStatus == MembershipDeltaStatus.NoChanges)
            {
                await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    Message = new LogMessage
                    {
                        Message = $"There are no membership changes for TargetOfficeGroupId {request.SyncJob.TargetOfficeGroupId}.",
                        RunId = runId
                    }
                });

                var sourceTypeCounts = JsonParser.GetQueryTypes(request.SyncJob.Query);
                var destination = JsonParser.GetDestination(request.SyncJob.Destination);
                var syncCompleteEvent = new SyncCompleteCustomEvent
                {
                    Type = destination.Type.ToString(),
                    SourceTypesCounts = sourceTypeCounts,
                    Destination = request.SyncJob.Destination,
                    RunId = runId.ToString(),
                    IsDryRunEnabled = false.ToString(),
                    ProjectedMemberCount = "0",
                    MembersToAdd = "0",
                    MembersToRemove = "0",
                    MembersAdded = "0",
                    MembersRemoved = "0",
                    MembersToAddNotFound = "0",
                    MembersToRemoveNotFound = "0",
                    IsInitialSync = $"{request.SyncJob.LastRunTime == SqlDateTime.MinValue.Value}"
                };

                var dbSyncJob = await context.CallActivityAsync<SyncJob>(nameof(JobReaderFunction),
                                       new JobReaderRequest
                                       {
                                           JobId = request.SyncJob.Id,
                                           RunId = request.SyncJob.RunId.GetValueOrDefault()
                                       });

                if (!context.IsReplaying)
                    TrackSyncCompleteEvent(context, dbSyncJob, syncCompleteEvent, "Success");

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                new JobStatusUpdaterRequest
                                {
                                    SyncJob = request.SyncJob,
                                    Status = SyncStatus.Idle,
                                    DeltaStatus = MembershipDeltaStatus.NoChanges
                                });
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

        private FileUploaderRequest CreateAggregatedFileUploaderRequest(GroupMembership membership, DeltaCalculatorResponse deltaResponse, SyncJob syncJob, IDurableOrchestrationContext context)
        {
            var membersToAdd = JsonConvert.DeserializeObject<ICollection<AzureADUser>>(TextCompressor.Decompress(deltaResponse.CompressedMembersToAddJSON));
            var membersToRemove = JsonConvert.DeserializeObject<ICollection<AzureADUser>>(TextCompressor.Decompress(deltaResponse.CompressedMembersToRemoveJSON));

            var newMembership = (GroupMembership)membership.Clone();
            newMembership.SourceMembers.Clear();
            newMembership.SourceMembers.AddRange(membersToAdd);
            newMembership.SourceMembers.AddRange(membersToRemove);

            var serializerSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore, // Ignores null values during serialization
                DefaultValueHandling = DefaultValueHandling.Ignore
            };

            var filePath = GenerateFileName(syncJob, "Aggregated", context);
            var content = TextCompressor.Compress(JsonConvert.SerializeObject(newMembership, serializerSettings));

            return new FileUploaderRequest { FilePath = filePath, Content = content, SyncJob = syncJob };
        }

        private string GenerateFileName(SyncJob syncJob, string suffix, IDurableOrchestrationContext context)
        {
            var timeStamp = context.CurrentUtcDateTime.ToString("MMddyyyy-HHmm");
            return $"/{syncJob.TargetOfficeGroupId}/{timeStamp}_{syncJob.RunId}_{suffix}.json";
        }

        private void TrackSyncCompleteEvent(IDurableOrchestrationContext context, SyncJob syncJob, SyncCompleteCustomEvent syncCompleteEvent, string successStatus)
        {
            var timeElapsedForJob = (context.CurrentUtcDateTime - syncJob.LastSuccessfulStartTime).TotalSeconds;
            _telemetryClient.TrackMetric(nameof(Services.Entities.Metric.SyncJobTimeElapsedSeconds), timeElapsedForJob);

            syncCompleteEvent.SyncJobTimeElapsedSeconds = timeElapsedForJob.ToString();
            syncCompleteEvent.Result = successStatus;

            var syncCompleteDict = syncCompleteEvent.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .ToDictionary(prop => prop.Name, prop => (string)prop.GetValue(syncCompleteEvent, null));

            _telemetryClient.TrackEvent(nameof(Services.Entities.Metric.SyncComplete), syncCompleteDict);
        }
    }
}
