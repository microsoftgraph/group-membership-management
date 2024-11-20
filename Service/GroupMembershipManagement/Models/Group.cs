// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Models
{
    public class Group
    {
        [ForeignKey("SyncJob")]
        public Guid SyncJobId { get; set; }

        public Guid GroupId { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}