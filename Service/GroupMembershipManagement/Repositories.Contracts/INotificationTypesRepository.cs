// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface INotificationTypesRepository
    {
        Task<NotificationType> GetNotificationTypeByNotificationTypeName(string notificationTypeName);

    }
}       

