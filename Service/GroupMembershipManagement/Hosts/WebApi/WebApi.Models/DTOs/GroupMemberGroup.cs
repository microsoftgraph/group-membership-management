// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public class GroupMemberGroup
    {
        public GroupMemberGroup(Guid objectId, string displayName)
        {
            ObjectId = objectId;
            DisplayName = displayName;
        }

        public Guid ObjectId { get; set; }
        public string DisplayName { get; set; }
    }
}