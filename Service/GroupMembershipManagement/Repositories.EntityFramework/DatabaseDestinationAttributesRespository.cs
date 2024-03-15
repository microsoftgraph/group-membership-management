// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework
{
    public class DatabaseDestinationAttributesRespository : IDatabaseDestinationAttributesRepository
    {
        private readonly GMMContext _writeContext;
        private readonly GMMReadContext _readContext;

        public DatabaseDestinationAttributesRespository(GMMContext writeContext, GMMReadContext readContext)
        {
            _writeContext = writeContext ?? throw new ArgumentNullException(nameof(writeContext));
            _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        }
        public async Task UpdateAttributes(DestinationAttributes destinationAttributes)
        {

            var job = await _writeContext.SyncJobs
                .Include(p => p.DestinationOwners)
                    .ThenInclude(owner => owner.SyncJobs)
                .Include(p => p.DestinationName)
                .SingleOrDefaultAsync(job => job.Id == destinationAttributes.Id);

            if (job == null)
            {
                return;
            }

            if (destinationAttributes.Name != null)
            {
                if (job.DestinationName != null)
                {
                    job.DestinationName.Name = destinationAttributes.Name;
                }
                else
                {
                    var destinationName = new DestinationName
                    {
                        Name = destinationAttributes.Name,
                        LastUpdatedTime = DateTime.UtcNow,
                        SyncJob = job
                    };
                    job.DestinationName = destinationName;
                }
            }

            if (destinationAttributes.Owners != null)
            {
                // Add owners that do not already exist
                foreach (var ownerId in destinationAttributes.Owners)
                {
                    if (!job.DestinationOwners.Any(o => o.ObjectId == ownerId))
                    {
                        var existingOwner = await _writeContext.DestinationOwners
                            .Include(o => o.SyncJobs)
                            .SingleOrDefaultAsync(o => o.ObjectId == ownerId);

                        if (existingOwner == null)
                        {
                            job.DestinationOwners.Add(new DestinationOwner
                            {
                                ObjectId = ownerId,
                                LastUpdatedTime = DateTime.UtcNow,
                                SyncJobs = new List<SyncJob> { job }
                            });
                        }
                        else
                        {
                            existingOwner.SyncJobs.Add(job);
                            job.DestinationOwners.Add(existingOwner);
                        }
                    }

                }

                var ownersToDelete = job.DestinationOwners.Where(o => !destinationAttributes.Owners.Contains(o.ObjectId)).ToList();

                // Remove owners that are no longer owners
                foreach (var owner in ownersToDelete)
                {
                    job.DestinationOwners.Remove(owner);
                    owner.SyncJobs.Remove(job);

                    if (!owner.SyncJobs.Any())
                    {
                        _writeContext.DestinationOwners.Remove(owner);
                    }
                }

                _writeContext.SaveChanges();
            }
        }
    }
}
