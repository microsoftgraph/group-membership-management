using Entities;
using Repositories.Contracts;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Services
{
    public class MockLoggingRepository : ILoggingRepository
    {
        public List<LogMessage> MessagesLogged { get; set; } = new List<LogMessage>();
        public int MessagesLoggedCount => MessagesLogged.Count;

        public Dictionary<string, string> SyncJobProperties { get; set; }
        public bool DryRun { get; set; }

        public Task LogMessageAsync(LogMessage logMessage, [CallerMemberName] string caller = "", [CallerFilePath] string file = "")
        {
            MessagesLogged.Add(logMessage);
            return Task.CompletedTask;
        }

        public Task LogPIIMessageAsync(LogMessage logMessage, [CallerMemberName] string caller = "", [CallerFilePath] string file = "")
        {
            MessagesLogged.Add(logMessage);
            return Task.CompletedTask;
        }
    }
}
