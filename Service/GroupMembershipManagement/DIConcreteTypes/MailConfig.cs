// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class MailConfig : IMailConfig
    {
        public bool IsAdaptiveCardEnabled { get; set; }
        public bool GMMHasSendMailApplicationPermissions { get; set; }
        public string SenderAddress { get; set; }

        public MailConfig(bool isAdaptiveCardEnabled, bool gmmHasSendMailApplicationPermissions, string senderAddress)
        {
            IsAdaptiveCardEnabled = isAdaptiveCardEnabled;
            GMMHasSendMailApplicationPermissions = gmmHasSendMailApplicationPermissions;
            SenderAddress = senderAddress;
        }

        public MailConfig()
        {
        }
    }
}
