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
    public class MockDisabledJobNotificationRepository : IDisabledJobNotificationRepository
    {
        private readonly Dictionary<(Guid, int), bool> _disabledJobNotification;

        public MockDisabledJobNotificationRepository(Dictionary<(Guid, int), bool> disabledJobNotification = null)
        {
			_disabledJobNotification = disabledJobNotification ?? new Dictionary<(Guid, int), bool>();
        }

        public Task<bool> IsNotificationDisabledForJob(Guid jobId, int notificationTypeId)
        {
            if (_disabledJobNotification.TryGetValue((jobId, notificationTypeId), out bool isDisabled))
            {
                return Task.FromResult(isDisabled);
            }
            return Task.FromResult(false);
        }

    }
}