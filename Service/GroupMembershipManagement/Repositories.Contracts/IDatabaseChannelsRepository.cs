// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IDatabaseChannelsRepository
    {
        Task<Channel> GetChannelAsync(Guid groupId, string channelId);
        Task<Channel> GetChannelUsingSyncJobIdAsync(Guid syncJobId);
    }
}