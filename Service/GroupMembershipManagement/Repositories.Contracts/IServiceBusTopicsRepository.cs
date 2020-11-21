using Entities;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IServiceBusTopicsRepository
    {
        Task AddMessageAsync(SyncJob job);
    }
}
