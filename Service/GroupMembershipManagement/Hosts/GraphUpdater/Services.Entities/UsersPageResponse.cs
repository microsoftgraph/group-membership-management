// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System.Collections.Generic;

namespace Services.Entities
{
    public class UsersPageResponse
    {
        public string NextPageUrl { get; set; }
        public List<AzureADUser> Members { get; set; }
        public Dictionary<string, int> NonUserGraphObjects { get; set; }
    }
}
