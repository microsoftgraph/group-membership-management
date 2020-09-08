// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class KeyVaultSecret<T> : IKeyVaultSecret<T>
    {
        public string Secret { get; private set; }
        public KeyVaultSecret(string secret)
        {
            Secret = secret;
        }
    }
    public class KeyVaultSecret<T, TSecret> : IKeyVaultSecret<T, TSecret>
    {
        public TSecret Secret { get; private set; }
        public KeyVaultSecret(TSecret secret)
        {
            Secret = secret;
        }
    }
}
