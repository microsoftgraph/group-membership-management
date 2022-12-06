// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Core;
using Entities;
using Entities.ServiceBus;
using GraphUpdater.Entities;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
	public class OrchestratorFunction
	{
		private const string SyncCompletedEmailBody = "SyncCompletedEmailBody";
		private readonly TelemetryClient _telemetryClient;
		private readonly IGraphUpdaterService _graphUpdaterService = null;
		private readonly IEmailSenderRecipient _emailSenderAndRecipients = null;
		private readonly IGMMResources _gmmResources = null;
		private readonly ILoggingRepository _loggingRepository = null;
		private readonly IDeltaCachingConfig _deltaCachingConfig = null;

		enum Metric
		{
			SyncComplete,
			SyncJobTimeElapsedSeconds
		}

		public OrchestratorFunction(
			TelemetryClient telemetryClient,
			IGraphUpdaterService graphUpdaterService,
			IEmailSenderRecipient emailSenderAndRecipients,
			IGMMResources gmmResources,
			ILoggingRepository loggingRepository,
            IDeltaCachingConfig deltaCachingConfig)
		{
			_telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
			_graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
			_emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients));
			_gmmResources = gmmResources ?? throw new ArgumentNullException(nameof(gmmResources));
			_loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _deltaCachingConfig = deltaCachingConfig ?? throw new ArgumentNullException(nameof(deltaCachingConfig));
		}

		[FunctionName(nameof(OrchestratorFunction))]
		public async Task<OrchestrationRuntimeStatus> RunOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
		{
			GroupMembership groupMembership = null;
			MembershipHttpRequest graphRequest = null;
			SyncJob syncJob = null;
			var sourceUsersNotFound = new List<AzureADUser>();
			var destinationUsersNotFound = new List<AzureADUser>();
			var syncCompleteEvent = new SyncCompleteCustomEvent();

			graphRequest = context.GetInput<MembershipHttpRequest>();


			try
			{
				syncJob = await context.CallActivityAsync<SyncJob>(nameof(JobReaderFunction),
													   new JobReaderRequest
													   {
														   JobPartitionKey = graphRequest.SyncJob.PartitionKey,
														   JobRowKey = graphRequest.SyncJob.RowKey,
														   RunId = graphRequest.SyncJob.RunId.GetValueOrDefault()
													   });

				var queries = JArray.Parse(syncJob.Query);
				var queryTypes = queries.SelectTokens("$..type")
										.Select(x => x.Value<string>())
										.ToList();

				syncCompleteEvent.Type = queryTypes.Distinct().Count() == 1 ? queryTypes[0] : "Hybrid";
				syncCompleteEvent.TargetOfficeGroupId = syncJob.TargetOfficeGroupId.ToString();
				syncCompleteEvent.RunId = syncJob.RunId.ToString();
				syncCompleteEvent.IsDryRunEnabled = false.ToString();
				syncCompleteEvent.ProjectedMemberCount = graphRequest.ProjectedMemberCount.HasValue ? graphRequest.ProjectedMemberCount.ToString() : "Not provided";

				var fileContent = await context.CallActivityAsync<string>(nameof(FileDownloaderFunction),
																			new FileDownloaderRequest
																			{
																				FilePath = graphRequest.FilePath,
																				SyncJob = syncJob
																			});

				groupMembership = JsonConvert.DeserializeObject<GroupMembership>(fileContent);

				await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorFunction)} function started", SyncJob = syncJob, Verbosity = VerbosityLevel.DEBUG });
				await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest
				{
					Message = $"Received membership from StarterFunction and will sync the obtained " +
																							  $"{groupMembership.SourceMembers.Distinct().Count()} distinct members",
					SyncJob = syncJob
				});

				var isValidGroup = await context.CallActivityAsync<bool>(nameof(GroupValidatorFunction),
										   new GroupValidatorRequest
										   {
											   RunId = groupMembership.RunId,
											   GroupId = groupMembership.Destination.ObjectId,
											   JobPartitionKey = groupMembership.SyncJobPartitionKey,
											   JobRowKey = groupMembership.SyncJobRowKey
										   });

				if (!isValidGroup)
				{
					await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
									CreateJobStatusUpdaterRequest(groupMembership.SyncJobPartitionKey, groupMembership.SyncJobRowKey,
																	SyncStatus.DestinationGroupNotFound, syncJob.ThresholdViolations, groupMembership.RunId));

					await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorFunction)} function did not complete", SyncJob = syncJob });

					return OrchestrationRuntimeStatus.Completed;
				}

				var isInitialSync = syncJob.LastRunTime == DateTime.FromFileTimeUtc(0);
				syncCompleteEvent.IsInitialSync = isInitialSync.ToString();
				var membersToAdd = groupMembership.SourceMembers.Where(x => x.MembershipAction == MembershipAction.Add).Distinct().ToList();
				syncCompleteEvent.MembersToAdd = membersToAdd.Count.ToString();
				var membersToRemove = groupMembership.SourceMembers.Where(x => x.MembershipAction == MembershipAction.Remove).Distinct().ToList();
				syncCompleteEvent.MembersToRemove = membersToRemove.Count.ToString();

                var membersAddedResponse = await context.CallSubOrchestratorAsync<GroupUpdaterSubOrchestratorResponse>(nameof(GroupUpdaterSubOrchestratorFunction),
                                CreateGroupUpdaterRequest(syncJob, membersToAdd, RequestType.Add, isInitialSync));
                syncCompleteEvent.MembersAdded = membersAddedResponse.SuccessCount.ToString();
				sourceUsersNotFound = membersAddedResponse.UsersNotFound;

                var membersRemovedResponse = await context.CallSubOrchestratorAsync<GroupUpdaterSubOrchestratorResponse>(nameof(GroupUpdaterSubOrchestratorFunction),
                                CreateGroupUpdaterRequest(syncJob, membersToRemove, RequestType.Remove, isInitialSync));
                syncCompleteEvent.MembersRemoved = membersRemovedResponse.SuccessCount.ToString();
                destinationUsersNotFound = membersRemovedResponse.UsersNotFound;


                if (isInitialSync)
				{
					var groupName = await context.CallActivityAsync<string>(nameof(GroupNameReaderFunction),
													new GroupNameReaderRequest { RunId = groupMembership.RunId, GroupId = groupMembership.Destination.ObjectId });

					var groupOwners = await context.CallActivityAsync<List<User>>(nameof(GroupOwnersReaderFunction),
													new GroupOwnersReaderRequest { RunId = groupMembership.RunId, GroupId = groupMembership.Destination.ObjectId });

					var ownerEmails = string.Join(";", groupOwners.Where(x => !string.IsNullOrWhiteSpace(x.Mail)).Select(x => x.Mail));

					var additionalContent = new[]
					{
								groupName,
								groupMembership.Destination.ObjectId.ToString(),
								membersToAdd.Count.ToString(),
								membersToRemove.Count.ToString(),
								syncJob.Requestor,
								_gmmResources.LearnMoreAboutGMMUrl,
								_emailSenderAndRecipients.SupportEmailAddresses
					};

					await context.CallActivityAsync(nameof(EmailSenderFunction),
													new EmailSenderRequest
													{
														ToEmail = ownerEmails,
														CcEmail = _emailSenderAndRecipients.SyncCompletedCCAddresses,
														ContentTemplate = SyncCompletedEmailBody,
														AdditionalContentParams = additionalContent,
														RunId = groupMembership.RunId
													});
				}


				var message = GetUsersDataMessage(groupMembership.Destination.ObjectId, membersToAdd.Count, membersToRemove.Count);
				await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = message, SyncJob = syncJob });

				await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
									CreateJobStatusUpdaterRequest(groupMembership.SyncJobPartitionKey, groupMembership.SyncJobRowKey,
																	SyncStatus.Idle, 0, groupMembership.RunId));

                if (!context.IsReplaying)
                {
                    if (membersAddedResponse.SuccessCount + membersAddedResponse.UsersNotFound.Count == membersToAdd.Count && 
						membersRemovedResponse.SuccessCount + membersRemovedResponse.UsersNotFound.Count == membersToRemove.Count)
                    {
                        TrackSyncCompleteEvent(context, syncJob, syncCompleteEvent, "Success");
                    }
                    else
                    {
                        TrackSyncCompleteEvent(context, syncJob, syncCompleteEvent, "PartialSuccess");
                    }
                }

                if (_deltaCachingConfig.DeltaCacheEnabled) await UpdateCacheAsync(context, sourceUsersNotFound, destinationUsersNotFound, syncJob, groupMembership.SourceMembers);

                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorFunction)} function completed", SyncJob = syncJob, Verbosity = VerbosityLevel.DEBUG });

				return OrchestrationRuntimeStatus.Completed;
			}
			catch (Exception ex)
			{
				await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"Caught unexpected exception, marking sync job as errored. Exception:\n{ex}", SyncJob = syncJob });

				if (syncJob == null)
				{
					await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = "SyncJob is null. Removing the message from the queue..." });
					return OrchestrationRuntimeStatus.Failed;
				}

				if (syncJob != null && groupMembership != null && !string.IsNullOrWhiteSpace(groupMembership.SyncJobPartitionKey) && !string.IsNullOrWhiteSpace(groupMembership.SyncJobRowKey))
				{
					await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
									CreateJobStatusUpdaterRequest(groupMembership.SyncJobPartitionKey, groupMembership.SyncJobRowKey,
																	SyncStatus.Error, syncJob.ThresholdViolations, groupMembership.RunId));
				}

                TrackSyncCompleteEvent(context, syncJob, syncCompleteEvent, "Failure");

				throw;
			}
			finally
			{
				if (syncJob?.RunId.HasValue ?? false)
					_loggingRepository.RemoveSyncJobProperties(syncJob.RunId.Value);
			}
		}

		public async Task UpdateCacheAsync(IDurableOrchestrationContext context,
												List<AzureADUser> sourceUsersNotFound,
												List<AzureADUser> destinationUsersNotFound,
												SyncJob syncJob,
												List<AzureADUser> sourceMembers)
		{
			if (sourceUsersNotFound != null && destinationUsersNotFound != null)
			{
				var totalUsersNotFound = sourceUsersNotFound.Union(destinationUsersNotFound).ToList();

				if (!context.IsReplaying & totalUsersNotFound.Count > 0) { TrackUsersNotFoundEvent(syncJob.RunId, totalUsersNotFound.Count, syncJob.TargetOfficeGroupId); }

				var sourceObjectIds = new HashSet<Guid>(sourceUsersNotFound.Select(emp => emp.ObjectId));
				var sourceUsers = sourceMembers.Where(product => sourceObjectIds.Contains(product.ObjectId)).ToList();
				var destinationObjectIds = new HashSet<Guid>(destinationUsersNotFound.Select(emp => emp.ObjectId));
				var destinationUsers = sourceMembers.Where(product => destinationObjectIds.Contains(product.ObjectId)).ToList();

				if (sourceUsers.Count > 0)
				{
					var sourceGroups = sourceUsers
									.SelectMany(u => u.SourceGroups.Select(c => (ObjectId: u, SourceGroup: c)))
									.GroupBy(x => x.SourceGroup)
									.Select(g => new GroupInfo { GroupId = g.Key, UserIds = g.Select(x => x.ObjectId).Distinct().ToList() }).ToList();

                    sourceGroups.RemoveAll(g => g.GroupId == Guid.Empty);

                    if (sourceGroups != null && sourceGroups.Count > 0)
					{
						// multiple source group processing flows in parallel
						var processingTasks = new List<Task>();
						foreach (var sourceGroup in sourceGroups)
						{
							var processTask = context.CallSubOrchestratorAsync(nameof(CacheUserUpdaterSubOrchestratorFunction),
								new CacheUserUpdaterRequest
								{
									GroupId = sourceGroup.GroupId,
									UserIds = sourceGroup.UserIds,
									RunId = syncJob.RunId,
									SyncJob = syncJob
								});
							processingTasks.Add(processTask);
						}

						await Task.WhenAll(processingTasks);
					}
				}

				if (destinationUsers.Count > 0)
				{
					await context.CallSubOrchestratorAsync(
						nameof(CacheUserUpdaterSubOrchestratorFunction),
						new CacheUserUpdaterRequest
						{
							GroupId = syncJob.TargetOfficeGroupId,
							UserIds = destinationUsers,
							RunId = syncJob.RunId,
							SyncJob = syncJob
						});
				}
			}
		}

		private void TrackUsersNotFoundEvent(Guid? runId, int usersNotFoundCount, Guid groupId)
		{
			var usersNotFoundEvent = new Dictionary<string, string>
			{
				{ "RunId", runId.ToString() },
				{ "TargetGroupId", groupId.ToString() },
				{ "UsersNotFound", usersNotFoundCount.ToString() }
			};
			_telemetryClient.TrackEvent("UsersNotFoundCount", usersNotFoundEvent);
		}

		private void TrackSyncCompleteEvent(IDurableOrchestrationContext context, SyncJob syncJob, SyncCompleteCustomEvent syncCompleteEvent, string successStatus)
		{
			var timeElapsedForJob = (context.CurrentUtcDateTime - syncJob.Timestamp.GetValueOrDefault()).TotalSeconds;
			_telemetryClient.TrackMetric(nameof(Metric.SyncJobTimeElapsedSeconds), timeElapsedForJob);

            syncCompleteEvent.SyncJobTimeElapsedSeconds = timeElapsedForJob.ToString();
            syncCompleteEvent.Result = successStatus;

			var syncCompleteDict = syncCompleteEvent.GetType()
				.GetProperties(BindingFlags.Instance | BindingFlags.Public)
				.ToDictionary(prop => prop.Name, prop => (string)prop.GetValue(syncCompleteEvent, null));

			_telemetryClient.TrackEvent(nameof(Metric.SyncComplete), syncCompleteDict);
		}

		private JobStatusUpdaterRequest CreateJobStatusUpdaterRequest(string partitionKey, string rowKey, SyncStatus syncStatus, int thresholdViolations, Guid runId)
		{
			return new JobStatusUpdaterRequest
			{
				RunId = runId,
				JobPartitionKey = partitionKey,
				JobRowKey = rowKey,
				Status = syncStatus,
				ThresholdViolations = thresholdViolations
			};
		}

		private GroupUpdaterRequest CreateGroupUpdaterRequest(SyncJob syncJob, ICollection<AzureADUser> members, RequestType type, bool isInitialSync)
		{
			return new GroupUpdaterRequest
			{
				SyncJob = syncJob,
				Members = members,
				Type = type,
				IsInitialSync = isInitialSync
			};
		}

		private string GetUsersDataMessage(Guid targetGroupId, int membersToAdd, int membersToRemove)
		{
			return $"Synchronization for {targetGroupId} is now complete. " +
				   $"{membersToAdd} users have been added. " +
				   $"{membersToRemove} users have been removed.";
		}
	}
}
