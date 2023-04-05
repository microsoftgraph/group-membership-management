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
        public Task<List<AzureADTeamsUser>> ReadUsersFromChannel(AzureADTeamsChannel teamsChannel, Guid runId);
        public Task<string> GetChannelType(AzureADTeamsChannel teamsChannel, Guid runId);
    }
}

