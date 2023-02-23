// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System.Collections.Generic;

namespace SecurityGroup.SubOrchestrator
{
    public class UsersReaderResponse
    {
        public List<AzureADUser> Users { get; set; }
        public string DeltaUrl { get; set; }
    }
}
