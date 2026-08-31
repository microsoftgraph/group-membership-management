// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Repositories.Contracts.DestinationResolution
{
    public class ResolvedGroupDestination : ResolvedDestination
    {
        public Guid ObjectId { get; init; }
        public override DestinationType DestinationType => DestinationType.Group;
    }
}
