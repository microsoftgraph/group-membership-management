// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using WebApi.Models.DTOs;
using Services.Messages.Contracts.Responses;

namespace Services.Messages.Responses
{
    public class GetGroupMembersResponse : ResponseBase
    {
        public Guid GroupId { get; set; }
        public int GroupMemberCount { get; set; }
        public List<GroupMemberGroup> Groups { get; set; } = new();
    }
}