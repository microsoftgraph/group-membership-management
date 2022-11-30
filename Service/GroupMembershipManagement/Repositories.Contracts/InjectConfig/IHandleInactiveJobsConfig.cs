// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Repositories.Contracts.InjectConfig
{
    public interface IHandleInactiveJobsConfig
    {
        public bool HandleInactiveJobsEnabled { get; }
    }
}
