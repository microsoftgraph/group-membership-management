// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;
using System.Net;
using System.Text.Json.Serialization;

namespace Services.Messages.Responses
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MembershipChangeType
    {
        Added,
        Removed,
    }

    public class SearchSyncHistoryByUserRunMembershipChange
    {
        public Guid RunId { get; set; }
        public MembershipChangeType MembershipChangeType { get; set; }
    }

    public class SearchSyncHistoryByUserResponse : ResponseBase
    {
        public List<Guid> MatchingRunIds { get; set; } = new();
        public List<SearchSyncHistoryByUserRunMembershipChange> RunMembershipChanges { get; set; } = new();
        public bool UserInCurrentGroup { get; set; }
        public bool CheckedCurrentGroupMembership { get; set; }
        public HttpStatusCode StatusCode { get; set; }
    }
}