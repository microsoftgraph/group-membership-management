// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetChannelRequest : RequestBase
    {
        public GetChannelRequest(Guid groupId, string channelId)
        {
            this.GroupId = groupId;
            this.ChannelId = channelId;
        }

        public Guid GroupId { get; }
        public string ChannelId { get; }
    }
}