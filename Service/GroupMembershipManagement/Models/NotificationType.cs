// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Models.CustomAttributes;

namespace Models
{
    [IgnoreLogging]
    public class NotificationType
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }
        public bool Disabled { get; set; }
    }
}