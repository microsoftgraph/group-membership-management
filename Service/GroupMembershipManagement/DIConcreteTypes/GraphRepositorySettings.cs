// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts;

namespace DIConcreteTypes
{
    public class GraphRepositorySettings : IGraphRepositorySettings
    {
        public int ConcurrentAddRequests { get; set; } = 10;
        public int ConcurrentRemoveRequests { get; set; } = 10;
    }
}