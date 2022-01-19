// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;

namespace Hosts.AzureUserReader
{
    public class AzureUserCreatorRequest
    {
        public List<string> PersonnelNumbers { get; set; }
        public TenantInformation TenantInformation { get; set; }
    }
}
