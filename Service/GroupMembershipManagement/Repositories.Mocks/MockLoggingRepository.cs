using Entities;
using Repositories.Contracts;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
    public class MockLoggingRepository : ILoggingRepository
    {
        public int MessagesLogged { get; private set; } = 0;
		public string SyncJobInfo { get; set; }

		public Task LogMessageAsync(LogMessage logMessage)
        {
            MessagesLogged++;
            return Task.CompletedTask;
        }
    }
}
