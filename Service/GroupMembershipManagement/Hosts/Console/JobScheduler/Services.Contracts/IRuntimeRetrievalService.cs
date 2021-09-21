// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IRuntimeRetrievalService
    {
        public Task<Dictionary<Guid, double>> GetRuntimes(List<Guid> groupIds);
    }
}
