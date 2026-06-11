// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class EmailSenderRecipient : IEmailSenderRecipient
    {
        public string SenderAddress { get; set; }
        public string SenderPassword { get; set; }
        public string SupportEmailAddresses { get; set; }

        public EmailSenderRecipient(string senderAddress, string senderPassword, string supportEmailAddresses)
        {
            SenderAddress = senderAddress;
            SenderPassword = senderPassword;
            SupportEmailAddresses = supportEmailAddresses;
        }

        public EmailSenderRecipient()
        {

        }
    }
}
