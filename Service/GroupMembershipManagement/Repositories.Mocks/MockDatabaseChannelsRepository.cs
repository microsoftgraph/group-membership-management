// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
    public class MockDatabaseChannelsRepository : IDatabaseChannelsRepository
    {
        public List<Channel> Channels { get; set; } = new List<Channel>();

        public async Task<Channel> GetChannelUsingSyncJobIdAsync(Guid syncJobId)
        {
            var channel = Channels.FirstOrDefault(x => x.SyncJobId == syncJobId);
            return await Task.FromResult(channel);
        }

        public async Task<Channel> GetChannelAsync(Guid syncJobId, string channelId)
        {
            var channel = Channels.FirstOrDefault(x => x.SyncJobId == syncJobId && x.ChannelId == channelId);
            return await Task.FromResult(channel);
        }
    }
}