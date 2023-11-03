// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts;
using System;

namespace DIConcreteTypes
{
    public class GMMEmailReceivers: IGMMEmailReceivers
    {
        public Guid ActionableMessageViewerGroupId { get; set; }

        public GMMEmailReceivers(Guid actionableMessageViewerGroupId)
        {
            ActionableMessageViewerGroupId = actionableMessageViewerGroupId;
        }

        public GMMEmailReceivers()
        {
        }
    }
}
