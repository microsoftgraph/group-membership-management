// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Repositories.Contracts
{
    public interface IGraphRepositorySettings
    {
        public int ConcurrentAddRequests { get; set; }
        public int ConcurrentRemoveRequests { get; set; }
    }
}
