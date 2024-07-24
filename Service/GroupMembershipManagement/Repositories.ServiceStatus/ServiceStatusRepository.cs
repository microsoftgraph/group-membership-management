// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.EntityFrameworkCore;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;
using ServiceStatusEntity = Entities.ServiceStatus;

namespace Repositories.ServiceStatus
{
    public class ServiceStatusRepository : IServiceStatusRepository
    {
        private readonly GMMContext _writeContext;
        private readonly GMMReadContext _readContext;

        public ServiceStatusRepository(GMMContext writeContext, GMMReadContext readContext)
        {
            _writeContext = writeContext ?? throw new ArgumentNullException(nameof(writeContext));
            _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        }

        public async Task<Models.ServiceStatuses> GetCurrentServiceStatusAsync()
        {
            var status = await _readContext.ServiceStatusHistory.Include(x => x.StatusDetails).OrderByDescending(s => s.Timestamp).FirstAsync();
            return status.StatusDetails.Status;
        }

        public Task SetServiceStatusAsync(Models.ServiceStatuses status, Guid requestorId)
        {
            _writeContext.ServiceStatusHistory.Add(new ServiceStatusHistory
            {
                RequestorObjectId = requestorId,
                StatusDetails = new ServiceStatusEntity
                {
                    Status = status
                }
            });

            return _writeContext.SaveChangesAsync();
        }
    }
}
