// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.SecurityGroup
{
    public class UsersSenderRequest
    {
        public SyncJob SyncJob { get; set; }
        public Guid RunId { get; set; }

        /// <summary>
        /// Compressed serialized List<AzureADUser>
        /// </summary>
        public string Users { get; set; }

        public int CurrentPart { get; set; }
        public bool Exclusionary { get; set; }
    }
}