// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.GraphUpdater
{
    public class FileUploaderRequest
    {
        public Guid RunId { get; set; }
        public SyncJob SyncJob { get; set; }
        public Guid ObjectId { get; set; }

        /// <summary>
        /// Compressed serialized List<AzureADUser>
        /// </summary>
        public string Users { get; set; }
    }
}