// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class StorageAccountSecret : IStorageAccountSecret
    {
        public StorageAccountSecret(string accountName)
        {
            AccountName = accountName;
        }

        public string AccountName { get; set; }
    }
}