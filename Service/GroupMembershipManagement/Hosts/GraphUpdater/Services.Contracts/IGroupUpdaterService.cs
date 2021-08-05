// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IGroupUpdaterService
    {
        Task<GraphUpdaterStatus> AddUsersToGroupAsync(ICollection<AzureADUser> members, Guid targetGroupId, Guid runId);
        Task<GraphUpdaterStatus> RemoveUsersFromGroupAsync(ICollection<AzureADUser> members, Guid targetGroupId, Guid runId);
    }
}
