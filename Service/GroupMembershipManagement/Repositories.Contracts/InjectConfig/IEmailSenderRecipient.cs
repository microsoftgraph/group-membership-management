// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Repositories.Contracts.InjectConfig
{
    public interface IEmailSenderRecipient
    {
        string SenderAddress { get; }
        string SenderPassword { get; }
        string SyncCompletedCCAddresses { get; }
        string SyncDisabledCCAddresses { get; }
        string SupportEmailAddresses { get; }
    }
}
