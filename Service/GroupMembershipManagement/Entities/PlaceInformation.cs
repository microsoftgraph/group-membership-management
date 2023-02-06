// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Graph;
using Models.Entities;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Entities
{
    [ExcludeFromCodeCoverage]
    public class PlaceInformation
    {
        public List<AzureADUser> Users { get; set; }
        public IGraphServicePlacesCollectionPage UsersFromPage { get; set; }
    }
}