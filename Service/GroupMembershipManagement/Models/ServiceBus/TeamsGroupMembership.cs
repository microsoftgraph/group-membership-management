// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Entities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json.Serialization;

namespace Models.ServiceBus
{
    [ExcludeFromCodeCoverage]
    public class TeamsGroupMembership
    {
        public AzureADGroup Destination { get; set; }

        /// <summary>
        /// Written after membership metadata so streaming readers can classify members as they arrive.
        /// </summary>
        [JsonPropertyOrder(100)]
        public List<AzureADTeamsUser> SourceMembers { get; set; } = new List<AzureADTeamsUser>();
        public Guid RunId { get; set; }
        public Guid SyncJobId { get; set; }
        public bool MembershipObtainerDryRunEnabled { get; set; }
        public bool Exclusionary { get; set; }
        public string Query { get; set; }

        /// <summary>
        /// Don't worry about setting this yourself, this is for Split and the serializer to set.
        /// </summary>
        public bool IsLastMessage { get; set; }
        public int TotalMessageCount { get; set; }

        /// <summary>
        /// This is made public mostly for testing, but you can use it to get an idea of how many GroupMemberships[] you'll get after calling Split if you want.
        /// </summary>
        public const int MembersPerChunk = 3765;
        public TeamsGroupMembership[] Split(int perChunk = MembersPerChunk)
        {
            var chunks = ChunksOfSize(SourceMembers, perChunk);
            var chunkCount = chunks.ToList().Count;

            var toReturn = chunks.
                Select(x => new TeamsGroupMembership
                {
                    Destination = Destination,
                    SyncJobId = SyncJobId,
                    SourceMembers = x,
                    RunId = RunId,
                    MembershipObtainerDryRunEnabled = MembershipObtainerDryRunEnabled,
                    Exclusionary = Exclusionary,
                    IsLastMessage = false,
                    TotalMessageCount = chunkCount
                }).ToArray();
            toReturn.Last().IsLastMessage = true;

            return toReturn;
        }

        public static GroupMembership Merge(IEnumerable<GroupMembership> groupMemberships)
        {
            return groupMemberships.Aggregate((acc, current) => { acc.SourceMembers.AddRange(current.SourceMembers); return acc; });
        }

        private static IEnumerable<List<T>> ChunksOfSize<T>(IEnumerable<T> enumerable, int chunkSize)
        {
            var toReturn = new List<T>();
            foreach (var item in enumerable)
            {
                if (toReturn.Count == chunkSize)
                {
                    yield return toReturn;
                    toReturn = new List<T>();
                }
                toReturn.Add(item);
            }
            yield return toReturn;
        }
    }
}
