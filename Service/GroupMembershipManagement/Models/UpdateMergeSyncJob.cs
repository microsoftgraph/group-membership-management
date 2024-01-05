// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Data.SqlTypes;

namespace Models
{
    public class UpdateMergeSyncJob
    {
        public Guid Id { get; set; }

        public DateTime StartDate { get; set; } = SqlDateTime.MinValue.Value;

        public UpdateMergeSyncJob() { }
    }
}
