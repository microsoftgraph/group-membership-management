using Entities;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface ILoggingRepository
    {
        public string SyncJobInfo { get; set; }
        Task LogMessageAsync(LogMessage logMessage);
    }
}
