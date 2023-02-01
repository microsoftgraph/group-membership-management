// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Graph;
using Models.Entities;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Entities
{
    [ExcludeFromCodeCoverage]
    public class GroupInformation
    {
        public List<AzureADUser> Users { get; set; }
        public Dictionary<string, int> NonUserGraphObjects { get; set; }
        public string NextPageUrl { get; set; }
        public IGroupTransitiveMembersCollectionWithReferencesPage UsersFromGroup { get; set; }
    }
}