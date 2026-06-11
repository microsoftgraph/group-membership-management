// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class PostGroupRequest : RequestBase
    {
        public PostGroupRequest(Guid userIdentity, string groupName, string groupAlias)
        {
            UserIdentity = userIdentity;
            GroupName = groupName;
            GroupAlias = groupAlias;
        }
        public Guid UserIdentity { get; }
        public string GroupName { get; }
        public string GroupAlias { get; }

    }
}