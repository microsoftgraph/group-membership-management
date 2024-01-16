// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Models
{
    public class DestinationValue
    {
        public Guid ObjectId { get; set; }
    }

    public class TeamsChannelDestinationValue : DestinationValue
    {
        public string ChannelId { get; set; }

        public override string ToString()
        {
            return $"{{ ObjectId: {ObjectId}, ChannelId: {ChannelId} }}";
        }
    }

    public class GroupDestinationValue : DestinationValue
    {
        public override string ToString()
        {
            return $"{{ ObjectId: {ObjectId} }}";
        }
    }

    public class DestinationObject
    {
        public string Type { get; set; }
        public DestinationValue Value { get; set; }

    }

}

