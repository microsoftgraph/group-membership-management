// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Repositories.Contracts
{
    public interface IGMMEmailReceivers
    {
        public Guid ActionableMessageViewerGroupId { get; }
    }
}
