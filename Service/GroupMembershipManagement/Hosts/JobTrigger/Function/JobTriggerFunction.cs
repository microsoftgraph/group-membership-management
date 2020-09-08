using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class JobTriggerFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly ISyncJobTopicService _syncJobTopicService = null;
        public JobTriggerFunction(ILoggingRepository loggingRepository, ISyncJobTopicService syncJobService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _syncJobTopicService = syncJobService ?? throw new ArgumentNullException(nameof(syncJobService)); ;
        }

        [FunctionName("JobTrigger")]
        public async Task Run([TimerTrigger("%jobTriggerSchedule%", RunOnStartup = true)]TimerInfo myTimer, ILogger log)
        {
            _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"JobTrigger function started at: {DateTime.UtcNow}" });
            await _syncJobTopicService.ProcessSyncJobsAsync();
            _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"JobTrigger function completed at: {DateTime.UtcNow}" });
        }
    }
}
