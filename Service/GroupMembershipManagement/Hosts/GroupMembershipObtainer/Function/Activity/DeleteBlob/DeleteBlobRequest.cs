// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;

namespace Hosts.GroupMembershipObtainer
{
    public class DeleteBlobRequest
    {
        public Guid GroupId { get; set; }
        public int CurrentPart { get; set; }
        public int TotalParts { get; set; }
        public SyncJob SyncJob { get; set; }

        /// <summary>
        /// Blob-name prefixes to delete. Null or empty deletes the part's page uploads under
        /// <c>{GroupId}/userUploads/{RunId}_GroupMembership_{CurrentPart}_</c>, as before this field existed.
        /// </summary>
        public List<string> Prefixes { get; set; }
    }
}