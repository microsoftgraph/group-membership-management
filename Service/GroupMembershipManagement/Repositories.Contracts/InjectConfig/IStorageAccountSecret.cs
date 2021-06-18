// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Repositories.Contracts.InjectConfig
{
    public interface IStorageAccountSecret
    {
        public string ConnectionString { get; set; }
    }
}