// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Repositories.Contracts.InjectConfig
{
    public interface IEmail
    {
        string SenderAddress { get; }
        string SenderPassword { get; }
        string SyncCompletedCCAddress { get; }
        string SyncDisabledCCAddress { get; }
    }
}
