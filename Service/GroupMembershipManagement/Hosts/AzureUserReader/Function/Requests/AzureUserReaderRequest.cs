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