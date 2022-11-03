// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IApplicationService
    {
        public Task RunAsync();
    }
}
