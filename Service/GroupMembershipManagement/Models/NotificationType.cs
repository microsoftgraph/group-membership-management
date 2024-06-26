// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Models.CustomAttributes;
using Models.Notifications;

namespace Models
{
    [IgnoreLogging]
    public class NotificationType
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public NotificationMessageType Name { get; set; }
        public bool Disabled { get; set; }
    }
}