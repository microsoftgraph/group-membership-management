// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Services.Entities
{
    public class JobsFilterSyncJob
    {
        public string? Query { get; set; }
        public Guid TargetOfficeGroupId { get; set; }
    }
}