// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Models.TeamsChannel
{
    public class TeamsDestination
    {
        public string Type { get; set; }
        public Guid ObjectId { get; set; }
        public string ChannelId { get; set; }
    }
}
