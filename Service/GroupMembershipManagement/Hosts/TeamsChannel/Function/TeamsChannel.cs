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

namespace Hosts.TeamsChannel
{
    public class TeamsChannel
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ISyncJobRepository _syncJobRepository;
        private readonly bool _isTeamsChannelEnabled;

        public TeamsChannel(ILoggingRepository loggingRepository, ISyncJobRepository syncJobRepository, IDryRunValue dryRun)
        {
            _loggingRepository = loggingRepository;
            _syncJobRepository = syncJobRepository;
            _isTeamsChannelEnabled = dryRun.DryRunEnabled;

        }

        [FunctionName("TeamsChannel")]
        public async Task RunAsync(
            [ServiceBusTrigger("%serviceBusSyncJobTopic%", "TeamsChannel", Connection = "serviceBusTopicConnection")] ServiceBusReceivedMessage message)
        {
            SyncJob syncJob = JsonConvert.DeserializeObject<SyncJob>(Encoding.UTF8.GetString(message.Body));

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"TeamsChannel recieved a message. Query: {syncJob.Query}.", RunId = syncJob.RunId });

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = "TeamsChannel finished.", RunId = syncJob.RunId });
        }
    }
}
