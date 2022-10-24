// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Graph;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Entities
{
    [ExcludeFromCodeCoverage]
    public class UserInformation
    {
        public List<AzureADUser> Users { get; set; }
        public Dictionary<string, int> NonUserGraphObjects { get; set; }
        public string NextPageUrl { get; set; }
        public IGraphServiceUsersCollectionPage UsersFromPage { get; set; }
    }
}