// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Repositories.Contracts.InjectConfig
{
    public interface IKeyVaultSecret<TType>
    {
        string Secret { get; }
    }
    public interface IKeyVaultSecret<TType, TSecret>
    {
        TSecret Secret { get; }
    }
}
