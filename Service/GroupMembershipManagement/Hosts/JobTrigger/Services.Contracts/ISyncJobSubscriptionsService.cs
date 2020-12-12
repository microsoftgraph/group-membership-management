using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface ISyncJobSubscriptionsService
    {
        Task ProcessSyncJobsAsync(string topicName, string subscriptionName);
    }
}
