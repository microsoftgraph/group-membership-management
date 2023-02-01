// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Graph;
using Models.Entities;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Entities
{
    [ExcludeFromCodeCoverage]
    public class DeltaGroupInformation
    {
        public List<AzureADUser> UsersToAdd { get; set; }
        public List<AzureADUser> UsersToRemove { get; set; }
        public string NextPageUrl { get; set; }
        public string DeltaUrl { get; set; }
        public IGroupDeltaCollectionPage UsersFromGroup { get; set; }
    }
}