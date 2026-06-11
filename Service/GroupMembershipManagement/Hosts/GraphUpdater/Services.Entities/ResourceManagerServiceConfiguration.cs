// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Services.Entities
{
    public class ResourceManagerServiceConfiguration
    {
        public string SubscriptionId { get; set; }
        public string DataResourceGroup { get; set; }
        public string ComputeResourceGroup { get; set; }
    }
}
