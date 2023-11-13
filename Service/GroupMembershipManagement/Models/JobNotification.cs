// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Models.CustomAttributes;  
using Models;

namespace Models
{
    [IgnoreLogging]
    public class JobNotification
    {
        [Key]
        public Guid Id { get; set; }

        [ForeignKey("SyncJob")]
        public Guid SyncJobId { get; set; } 

        [ForeignKey("NotificationType")]
        public int NotificationTypeID { get; set; }

        public bool Disabled { get; set; }

        // Navigation properties
        public virtual SyncJob SyncJob { get; set; }
        public virtual NotificationType NotificationType { get; set; }
    }
}