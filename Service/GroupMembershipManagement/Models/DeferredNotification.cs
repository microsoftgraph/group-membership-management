// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.ComponentModel.DataAnnotations;
using Models.Notifications;

namespace Models
{
    public class DeferredNotification
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public long SequenceNumber { get; set; }

        [Required]
        public NotificationMessageType MessageType { get; set; }

        [Required]
        public DeferredNotificationStatus Status { get; set; } = DeferredNotificationStatus.Deferred;

        [Required]
        public DateTime DeferredAt { get; set; } = DateTime.UtcNow;

        public DateTime? MessageExpiresAt { get; set; }

        public DateTime? ReplayedAt { get; set; }

        [MaxLength(500)]
        public string SuppressionReason { get; set; }

        public Guid? SyncJobId { get; set; }

        public Guid? RunId { get; set; }
    }

    public enum DeferredNotificationStatus
    {
        Deferred = 0,
        Replaying = 1,
        Replayed = 2,
        Expired = 3,
        Failed = 4
    }
}
