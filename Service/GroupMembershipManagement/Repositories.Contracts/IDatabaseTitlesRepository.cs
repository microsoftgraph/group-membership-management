// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IDatabaseTitlesRepository
    {
        Task SaveTitlesAsync(Dictionary<string, string> titlesDictionary, Guid syncJobId);
        Task<List<Title>> GetTitlesAsync(Guid syncJobId);
        Task UpdateTitlesAsync(List<Title> titles, Guid syncJobId);
        Task DeleteTitlesAsync(Guid syncJobId);
    }
}