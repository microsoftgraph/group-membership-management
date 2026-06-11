// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Models.Helpers
{
    public class DestinationParser
    {
        enum MembershipType
        {
            GroupMembership,
            TeamsChannelMembership
        }
        public static DestinationObject ParseDestination(SyncJob syncJob)
        {
            if (string.IsNullOrWhiteSpace(syncJob.MembershipType)) return null;

            string type = syncJob.MembershipType;

            if (type == MembershipType.TeamsChannelMembership.ToString())
            {
                if (syncJob.Channel == null) return null;

                return new DestinationObject
                {
                    Type = type,
                    Value = new TeamsChannelDestinationValue
                    {
                        ObjectId = syncJob.Channel.GroupId,
                        ChannelId = syncJob.Channel.ChannelId
                    }
                };
            }
            else if (type == MembershipType.GroupMembership.ToString())
            {
                if (syncJob.Group == null) return null;

                return new DestinationObject
                {
                    Type = type,
                    Value = new GroupDestinationValue
                    {
                        ObjectId = syncJob.Group.GroupId
                    }
                };
            }
            else
            {
                return null;
            }
        }
    }
}

