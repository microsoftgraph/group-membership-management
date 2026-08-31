// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Repositories.Contracts.DestinationResolution
{
    public enum DestinationType
    {
        Group,
        TeamsChannel
    }

    public abstract class ResolvedDestination
    {
        public abstract DestinationType DestinationType { get; }
        public Guid SyncJobId { get; init; }
    }
}
