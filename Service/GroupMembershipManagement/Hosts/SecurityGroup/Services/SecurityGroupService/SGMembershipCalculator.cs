// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Microsoft.Graph;
using Polly;
using Polly.Retry;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Hosts.SecurityGroup
{
	public class SGMembershipCalculator
	{
		private readonly IGraphGroupRepository _graphGroupRepository;
		private readonly IMembershipServiceBusRepository _membershipServiceBus;
		private readonly ILoggingRepository _log;
		private readonly IMailRepository _mail;
		private readonly IEmailSenderRecipient _emailSenderAndRecipients;
		private readonly ISyncJobRepository _syncJob;
		private readonly ILoggingRepository _log;
		private readonly bool _isSecurityGroupDryRunEnabled;

		private const string EmailSubject = "EmailSubject";
		private const string SyncDisabledNoGroupEmailBody = "SyncDisabledNoGroupEmailBody";
		private const string SyncDisabledNoValidGroupIds = "SyncDisabledNoValidGroupIds";


		public SGMembershipCalculator(IGraphGroupRepository graphGroupRepository, IMembershipServiceBusRepository membershipServiceBus, 
			IMailRepository mail, IEmailSenderRecipient emailSenderAndRecipients, ISyncJobRepository syncJob, ILoggingRepository logging,
			IDryRunValue dryRun)
		{
			_graphGroupRepository = graphGroupRepository;
			_membershipServiceBus = membershipServiceBus;
			_log = logging;
			_mail = mail;
			_syncJob = syncJob;
			_log = logging;
			_isSecurityGroupDryRunEnabled = dryRun.DryRunEnabled;
		}

		private const int NumberOfGraphRetries = 5;
		private AsyncRetryPolicy _graphRetryPolicy;
		private const string EmailSubject = "EmailSubject";

		public AzureADGroup[] ReadSourceGroups(SyncJob syncJob)
		{
			return syncJob.Query.Split(';').Select(x => Guid.TryParse(x, out var parsed) ? parsed : Guid.Empty)
										   .Where(x => x != Guid.Empty)
										   .Select(x => new AzureADGroup { ObjectId = x }).ToArray();
		}

		public async Task<PolicyResult<bool>> GroupExistsAsync(Guid objectId, Guid runId)
		{
			// make this fresh every time because the lambda has to capture the run ID
			_graphRetryPolicy = Policy.Handle<SocketException>().WaitAndRetryAsync(NumberOfGraphRetries, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
				   onRetry: async (ex, count) =>
				   {
					   await _log.LogMessageAsync(new LogMessage
					   {
						   Message = $"Got a transient SocketException. Retrying. This was try {count} out of {NumberOfGraphRetries}.\n" + ex.ToString(),
						   RunId = runId
					   });
				   });

			return await _graphRetryPolicy.ExecuteAndCaptureAsync(() => _graphGroupRepository.GroupExists(objectId));
		}

			var sourceGroups = syncJob.Query.Split(';').Select(x => Guid.TryParse(x, out var parsed) ? parsed : Guid.Empty)
				.Where(x => x != Guid.Empty)
				.Select(x => new AzureADGroup { ObjectId = x }).ToArray();

			await _log.LogMessageAsync(new LogMessage
			{
				RunId = runId,
				Message =
				$"Reading source groups {syncJob.Query} to be synced into the destination group {syncJob.TargetOfficeGroupId}."
			});


			List<AzureADUser> allusers = null;
			try
			{
				if (sourceGroups.Length == 0)
				{
					await _log.LogMessageAsync(new LogMessage
					{
						RunId = runId,
						Message =
						$"None of the source groups in {syncJob.Query} were valid guids. Marking job as errored."
					});

					await _mail.SendMailAsync(new EmailMessage
					{
						Subject = EmailSubject,
						Content = SyncDisabledNoValidGroupIds,
						SenderAddress = _emailSenderAndRecipients.SenderAddress,
						SenderPassword = _emailSenderAndRecipients.SenderPassword,
						ToEmailAddresses = syncJob.Requestor,
						CcEmailAddresses = _emailSenderAndRecipients.SyncDisabledCCAddresses,
						AdditionalContentParams = new[] { syncJob.Query }
					}, runId);
				}
				else
				{
					allusers = await GetUsersForEachGroup(sourceGroups, syncJob.Requestor, runId);
				}
			}
			catch (Exception ex)
			{
				await _log.LogMessageAsync(new LogMessage
				{
					Message = "Caught unexpected exception, marking sync job as errored. Exception:\n" + ex,
					RunId = runId
				});
				allusers = null;

				// make sure this gets thrown to where App Insights will handle it
				throw;
			}
			finally
			{
				if (allusers != null)
				{
					await _log.LogMessageAsync(new LogMessage
					{
						RunId = runId,
						Message =
						$"Read {allusers.Count} users from source groups {syncJob.Query} to be synced into the destination group {syncJob.TargetOfficeGroupId}."
					});

					await _membershipServiceBus.SendMembership(new GroupMembership
					{
						SourceMembers = allusers ?? new List<AzureADUser>(),
						Destination = new AzureADGroup { ObjectId = syncJob.TargetOfficeGroupId },
						RunId = runId,
						SyncJobRowKey = syncJob.RowKey,
						SyncJobPartitionKey = syncJob.PartitionKey,
						SyncDryRun = _isSecurityGroupDryRunEnabled ? true:false,
					});
				}
				else
				{
					await _syncJob.UpdateSyncJobStatusAsync(new[] { syncJob }, SyncStatus.Error);
				}

		public async Task<(List<AzureADUser> users,
						   Dictionary<string, int> nonUserGraphObjects,
						   string nextPageUrl,
						   IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)> GetNextUsersPageAsync(string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)
		{
			var result = await _graphGroupRepository.GetNextUsersPageAsync(nextPageUrl, usersFromGroup);
			return result;
		}

		public async Task SendMembershipAsync(SyncJob syncJob, Guid runId, List<AzureADUser> allusers)
		{
			await _membershipServiceBus.SendMembership(new GroupMembership
			{
				SourceMembers = allusers ?? new List<AzureADUser>(),
				Destination = new AzureADGroup { ObjectId = syncJob.TargetOfficeGroupId },
				RunId = runId,
				SyncJobRowKey = syncJob.RowKey,
				SyncJobPartitionKey = syncJob.PartitionKey
			});
		}

		public async Task SendEmailAsync(SyncJob job, Guid runId, string content, string[] additionalContentParams)
		{
			await _mail.SendMailAsync(new EmailMessage
			{
				Subject = EmailSubject,
				Content = content,
				SenderAddress = _emailSenderAndRecipients.SenderAddress,
				SenderPassword = _emailSenderAndRecipients.SenderPassword,
				ToEmailAddresses = job.Requestor,
				CcEmailAddresses = _emailSenderAndRecipients.SyncDisabledCCAddresses,
				AdditionalContentParams = additionalContentParams
			}, runId);
		}

		public async Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status)
		{
			await _syncJob.UpdateSyncJobStatusAsync(new[] { job }, status);
		}
	}
}