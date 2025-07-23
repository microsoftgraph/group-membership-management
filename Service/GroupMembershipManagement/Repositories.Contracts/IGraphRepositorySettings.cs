// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Repositories.Contracts
{
    public interface IGraphRepositorySettings
    {
        public int ConcurrentWriteRequests { get; set; }
    }
}
