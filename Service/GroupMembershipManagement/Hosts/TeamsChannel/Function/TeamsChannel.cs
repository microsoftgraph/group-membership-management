using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Azure.Messaging.ServiceBus;
using Entities;
using System.Text;
using Microsoft.Graph;
using TeamsChannel.Service.Contracts;
using Models.Entities;
using Newtonsoft.Json.Linq;
using System.Linq;
using TeamsChannel.Service;

namespace Hosts.TeamsChannel
{
    public class TeamsChannel
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ISyncJobRepository _syncJobRepository;
        private readonly ITeamsChannelService _teamsChannelService;
        private readonly bool _isTeamsChannelDryRunEnabled;

        public TeamsChannel(ILoggingRepository loggingRepository, ISyncJobRepository syncJobRepository, ITeamsChannelService teamsChannelService, IDryRunValue dryRun)
        {
            _loggingRepository = loggingRepository;
            _syncJobRepository = syncJobRepository;
            _teamsChannelService = teamsChannelService;
            _isTeamsChannelDryRunEnabled = dryRun.DryRunEnabled;

        }

        [FunctionName("TeamsChannel")]
        public async Task RunAsync(
            [ServiceBusTrigger("%serviceBusSyncJobTopic%", "TeamsChannel", Connection = "serviceBusTopicConnection")] ServiceBusReceivedMessage message)
        {
            var syncInfo = GetGroupInfo(message);

            var runId = syncInfo.SyncJob.RunId.GetValueOrDefault(Guid.Empty);
            _loggingRepository.SetSyncJobProperties(runId, syncInfo.SyncJob.ToDictionary());

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"TeamsChannel recieved a message. Query: {syncInfo.SyncJob.Query}.", RunId = runId }, VerbosityLevel.DEBUG);

            var users = await _teamsChannelService.GetUsersFromTeam(GetGroupInfo(message));
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Read {users.Count} from {syncInfo.SyncJob.Query}.", RunId = runId }, VerbosityLevel.DEBUG);

            // upload to blob storage

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Uplading {users.Count} from {syncInfo.SyncJob.Query} to blob storage.", RunId = runId }, VerbosityLevel.DEBUG);
            var filename = await _teamsChannelService.UploadMembership(users, syncInfo, _isTeamsChannelDryRunEnabled);

            // make HTTP call


            await _loggingRepository.LogMessageAsync(new LogMessage { Message = "TeamsChannel finished.", RunId = runId }, VerbosityLevel.DEBUG);
        }

        private ChannelSyncInfo GetGroupInfo(ServiceBusReceivedMessage message)
        {
            return new ChannelSyncInfo
            {
                SyncJob = JsonConvert.DeserializeObject<SyncJob>(Encoding.UTF8.GetString(message.Body)),
                Exclusionary = message.ApplicationProperties.ContainsKey("Exclusionary") ? Convert.ToBoolean(message.ApplicationProperties["Exclusionary"]) : false,
                CurrentPart = message.ApplicationProperties.ContainsKey("CurrentPart") ? Convert.ToInt32(message.ApplicationProperties["CurrentPart"]) : 0,
                TotalParts = message.ApplicationProperties.ContainsKey("TotalParts") ? Convert.ToInt32(message.ApplicationProperties["TotalParts"]) : 0,
                IsDestinationPart = message.ApplicationProperties.ContainsKey("IsDestinationPart") ? Convert.ToBoolean(message.ApplicationProperties["IsDestinationPart"]) : false,
            };
        }


    }
}
