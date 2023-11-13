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
    public class MockJobNotificationRepository : IJobNotificationsRepository
    {
        private readonly Dictionary<(Guid, int), bool> _jobNotification;

        public MockJobNotificationRepository(Dictionary<(Guid, int), bool> jobNotification = null)
        {
			_jobNotification = jobNotification ?? new Dictionary<(Guid, int), bool>();
        }

        public Task<bool> IsNotificationDisabledForJob(Guid jobId, int notificationTypeId)
        {
            if (_jobNotification.TryGetValue((jobId, notificationTypeId), out bool isDisabled))
            {
                return Task.FromResult(isDisabled);
            }
            return Task.FromResult(false);
        }

    }
}