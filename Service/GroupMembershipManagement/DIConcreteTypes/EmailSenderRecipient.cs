// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class EmailSenderRecipient : IEmailSenderRecipient
    {
        public string SenderAddress { get; set; }
        public string SenderPassword { get; set; }
        public string SyncCompletedCCAddresses { get; set; }
        public string SyncDisabledCCAddresses { get; set; }
        public EmailSenderRecipient(string senderAddress, string senderPassword, string syncCompletedCCAddresses, string syncDisabledCCAddresses)
        {
            this.SenderAddress = senderAddress;
            this.SenderPassword = senderPassword;
            this.SyncCompletedCCAddresses = syncCompletedCCAddresses;
            this.SyncDisabledCCAddresses = syncDisabledCCAddresses;
        }

        public EmailSenderRecipient()
        {

        }
    }
}
