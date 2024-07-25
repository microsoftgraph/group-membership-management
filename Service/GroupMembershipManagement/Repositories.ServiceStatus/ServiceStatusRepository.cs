// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.EntityFrameworkCore;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;

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
            return status.StatusDetails.Name;
        }

        public Task SetServiceStatusAsync(Models.ServiceStatuses status, Guid requestorId)
        {
            var statusDetails = _readContext.ServiceStatus.Single(s => s.Name == status);
            var statusHistory = new ServiceStatusHistory
            {
                RequestorObjectId = requestorId,
                ServiceStatusId = statusDetails.Id,
                Timestamp = DateTime.UtcNow
            };

            _writeContext.ServiceStatusHistory.Add(statusHistory);
            return _writeContext.SaveChangesAsync();
        }
    }
}
