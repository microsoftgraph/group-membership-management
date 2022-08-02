// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Entities.ServiceBus
{
    [ExcludeFromCodeCoverage]
    public class GroupMembership : ICloneable
    {
        public AzureADGroup Destination { get; set; }
        public List<AzureADUser> SourceMembers { get; set; } = new List<AzureADUser>();
        public Guid RunId { get; set; }
        public string SyncJobRowKey { get; set; }
        public string SyncJobPartitionKey { get; set; }
        public bool MembershipObtainerDryRunEnabled { get; set; }
        public bool Exclusionary { get; set; }

        /// <summary>
        /// Don't worry about setting this yourself, this is for Split and the serializer to set.
        /// </summary>
        public bool IsLastMessage { get; set; }
        public int TotalMessageCount { get; set; }

        /// <summary>
        /// This is made public mostly for testing, but you can use it to get an idea of how many GroupMemberships[] you'll get after calling Split if you want.
        /// </summary>
        public const int MembersPerChunk = 3765;
        public GroupMembership[] Split(int perChunk = MembersPerChunk)
        {
            var chunks = ChunksOfSize(SourceMembers, perChunk);
            var chunkCount = chunks.ToList().Count;

            var toReturn = chunks.
                Select(x => new GroupMembership
                {
                    Destination = Destination,
                    SyncJobPartitionKey = SyncJobPartitionKey,
                    SyncJobRowKey = SyncJobRowKey,
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

        /// <summary>
        /// Does a full deep clone
        /// </summary>
        /// <returns></returns>
        public object Clone()
        {
            var groupMembership = (GroupMembership)this.MemberwiseClone();
            groupMembership.Destination = this.Destination != null ? new AzureADGroup { ObjectId = this.Destination.ObjectId } : null;
            groupMembership.SyncJobPartitionKey = this.SyncJobPartitionKey;
            groupMembership.SyncJobRowKey = this.SyncJobRowKey;
            SourceMembers = this.SourceMembers != null
                            ? this.SourceMembers.Select(x => new AzureADUser
                            {
                                ObjectId = x.ObjectId,
                                MembershipAction = x.MembershipAction
                            }).ToList()
                            : null;

            return groupMembership;
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
