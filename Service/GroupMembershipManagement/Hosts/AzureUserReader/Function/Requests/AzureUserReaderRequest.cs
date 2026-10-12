// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Hosts.AzureUserReader
{
    public class AzureUserReaderRequest
    {
        public string ContainerName { get; set; }
        public string BlobPath { get; set; }
        public bool ShouldCreateNewUsers { get; set; }
        /// <summary>
        /// Required when ShouldCreateNewUsers = true
        /// </summary>
        public TenantInformation TenantInformation { get; set; }
        /// <summary>
        /// Number of demo agent identities to ensure exist. 0 skips the step.
        /// </summary>
        public int AgentCount { get; set; }
        /// <summary>
        /// Maximum demo agents managed by a single user. Must be at least 1.
        /// </summary>
        public int MaxAgentsPerUser { get; set; } = 1;
    }

    public class TenantInformation
    {
        /// <summary>
        /// Tenant Domain. i.e. M365x000000.OnMicrosoft.com
        /// </summary>
        public string TenantDomain { get; set; }
        public string EmailPrefix { get; set; }
        /// <summary>
        /// A two letter country code (ISO standard 3166).
        /// Examples include: 'US', 'JP', and 'GB'.
        /// </summary>
        public string CountryCode { get; set; }
    }
}