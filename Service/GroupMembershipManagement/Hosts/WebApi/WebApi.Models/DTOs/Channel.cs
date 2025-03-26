// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public class Channel
    {
        public Channel(Guid teamId, string channelId, string name)
        {
            TeamId = teamId;
            ChannelId = channelId;
            Name = name;
        }

        public Guid TeamId { get; set; }
        public string ChannelId { get; set; }
        public string Name { get; set; }
    }
}
