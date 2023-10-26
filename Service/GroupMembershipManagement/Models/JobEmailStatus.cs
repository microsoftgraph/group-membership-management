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
    public class JobEmailStatus
    {
        [Key]
        public Guid JobEmailStatusId { get; set; }

        [ForeignKey("SyncJob")]
        public Guid SyncJobId { get; set; } 

        [ForeignKey("EmailType")]
        public int EmailTypeId { get; set; }

        public bool DisableEmail { get; set; }

        // Navigation properties
        public virtual SyncJob SyncJob { get; set; }
        public virtual EmailType EmailType { get; set; }
    }
}