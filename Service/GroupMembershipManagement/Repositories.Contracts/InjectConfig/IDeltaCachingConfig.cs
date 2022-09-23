// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Repositories.Contracts.InjectConfig
{
    public interface IDeltaCachingConfig
    {
        public bool DeltaCacheEnabled { get; }
    }
}
