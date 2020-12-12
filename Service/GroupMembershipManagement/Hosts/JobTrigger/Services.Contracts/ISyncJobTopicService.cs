using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface ISyncJobTopicService
    {
        Task ProcessSyncJobsAsync();
    }
}
