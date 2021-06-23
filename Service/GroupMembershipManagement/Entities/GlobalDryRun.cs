// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Entities
{
    public enum GlobalDryRun
    {
        Off = 0,
        AllSecurityGroupsJobs = 1,
        AllOneCatalogJobs = 2,
        IndividualJobsWithDryRunEnabled = 3,
        All = 4
    }
}
