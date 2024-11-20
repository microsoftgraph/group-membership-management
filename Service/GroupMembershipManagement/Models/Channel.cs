// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Models
{
    public class Channel
    {
        [ForeignKey("SyncJob")]
        public Guid SyncJobId { get; set; }

        public string ChannelId { get; set; }
        public Guid GroupId { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}