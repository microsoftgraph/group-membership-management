// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface ITeamsChannelRepository
    {
        public Task<List<AzureADTeamsUser>> ReadUsersFromChannelAsync(AzureADTeamsChannel teamsChannel, Guid runId);
        public Task<string> GetChannelTypeAsync(AzureADTeamsChannel teamsChannel, Guid runId);
    }
}

