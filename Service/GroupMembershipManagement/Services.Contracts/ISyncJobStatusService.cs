using Models;
using Models.ServiceBus;
using Models.SyncJobHistory;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface ISyncJobStatusService
    {
        Task UpdateJobStatusAsync(SyncJob job, SyncStatus? status, SyncJobHistory? history = null);
        Task CreateOrUpdateJobHistoryAsync(SyncJobHistory history);
    }
}
