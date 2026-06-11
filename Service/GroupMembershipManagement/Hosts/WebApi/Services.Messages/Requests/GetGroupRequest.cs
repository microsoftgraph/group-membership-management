// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetGroupRequest : RequestBase
    {
        public GetGroupRequest(Guid groupId)
        {
            this.GroupId = groupId;
        }

        public Guid GroupId { get; }
    }
}