using Entities;
using Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IDatabaseJobEmailStatusesRepository
    {
        Task<bool> IsEmailDisabledForJob(Guid jobId, int emailTypeId)
    }
}
