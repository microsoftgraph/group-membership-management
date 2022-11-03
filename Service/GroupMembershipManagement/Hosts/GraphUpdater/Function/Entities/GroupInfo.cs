// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace GraphUpdater.Entities
{
    [ExcludeFromCodeCoverage]
    public class GroupInfo
    {
        public Guid GroupId { get; set; }

        public List<AzureADUser> UserIds { get; set; }
    }
}
