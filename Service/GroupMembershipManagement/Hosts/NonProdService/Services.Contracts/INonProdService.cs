// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface INonProdService
    {
        Task<bool> CreateTestGroups();
        Task<bool> FillTestGroups();
    }
}
