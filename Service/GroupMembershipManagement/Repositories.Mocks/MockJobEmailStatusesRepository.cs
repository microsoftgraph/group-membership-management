// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Polly;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
    public class MockJobEmailStatusesRepository : IDisabledJobNotificationRepository
    {
        private readonly Dictionary<(Guid, int), bool> _jobEmailStatuses;

        public MockJobEmailStatusesRepository(Dictionary<(Guid, int), bool> jobEmailStatuses = null)
        {
            _jobEmailStatuses = jobEmailStatuses ?? new Dictionary<(Guid, int), bool>();
        }

        public Task<bool> IsEmailDisabledForJob(Guid jobId, int emailTypeId)
        {
            if (_jobEmailStatuses.TryGetValue((jobId, emailTypeId), out bool isDisabled))
            {
                return Task.FromResult(isDisabled);
            }
            return Task.FromResult(false);
        }

    }
}