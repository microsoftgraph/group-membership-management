// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IJobNotificationsRepository
    {
        Task<bool> IsNotificationDisabledForJobAsync(Guid jobId, int notificationTypeId);
    }
}
