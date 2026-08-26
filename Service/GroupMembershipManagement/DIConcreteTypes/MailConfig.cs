// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class MailConfig : IMailConfig
    {
        public bool GMMHasSendMailApplicationPermissions { get; set; }
        public string SenderAddress { get; set; }
        public bool SkipEmailNotifications { get; set; }

        public MailConfig(bool gmmHasSendMailApplicationPermissions, string senderAddress, bool skipEmailNotifications)
        {
            GMMHasSendMailApplicationPermissions = gmmHasSendMailApplicationPermissions;
            SenderAddress = senderAddress;
            SkipEmailNotifications = skipEmailNotifications;
        }

        public MailConfig()
        {
        }
    }
}
