// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Services.Messages.Contracts.Requests;
namespace Services.Messages.Requests
{
    public class PostGroupRequest : RequestBase
    {
        public PostGroupRequest(Guid userIdentity, string groupName, string groupAlias, NewGroupType groupType = NewGroupType.Microsoft365)
        {
            UserIdentity = userIdentity;
            GroupName = groupName;
            GroupAlias = groupAlias;
            GroupType = groupType;
        }
        public Guid UserIdentity { get; }
        public string GroupName { get; }
        public string GroupAlias { get; }
        public NewGroupType GroupType { get; }
    }
}