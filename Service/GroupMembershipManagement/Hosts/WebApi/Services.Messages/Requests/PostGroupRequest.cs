// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Services.Messages.Contracts.Requests;
namespace Services.Messages.Requests
{
    public class PostGroupRequest : RequestBase
    {
        public PostGroupRequest(Guid userIdentity, string groupName, string groupAlias, NewGroupType groupType = NewGroupType.Microsoft365, string? description = null)
        {
            UserIdentity = userIdentity;
            GroupName = groupName;
            GroupAlias = groupAlias;
            GroupType = groupType;
            Description = description;
        }
        public Guid UserIdentity { get; }
        public string GroupName { get; }
        public string GroupAlias { get; }
        public NewGroupType GroupType { get; }
        public string? Description { get; }
    }
}