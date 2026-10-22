// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;

namespace Hosts.GroupMembershipObtainer
{
    public class CacheConverterRequest
    {
        public Guid ObjectId { get; set; }
        public string FilePath { get; set; }
        public int CurrentPart { get; set; }
        public int TotalParts { get; set; }
        public SyncJob SyncJob { get; set; }

        /// <summary>
        /// The replay-safe time of the conversion, used for the converted cache name. Null in histories
        /// scheduled before this field existed; the activity then uses the current UTC time.
        /// </summary>
        public DateTime? FileTimestamp { get; set; }
    }
}