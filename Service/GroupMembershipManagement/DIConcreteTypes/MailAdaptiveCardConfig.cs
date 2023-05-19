// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class MailAdaptiveCardConfig : IMailAdaptiveCardConfig
    {
        public bool IsAdaptiveCardEnabled { get; set; }

        public MailAdaptiveCardConfig(bool isAdaptiveCardEnabled)
        {
            IsAdaptiveCardEnabled = isAdaptiveCardEnabled;
        }

        public MailAdaptiveCardConfig()
        {
        }
    }
}
