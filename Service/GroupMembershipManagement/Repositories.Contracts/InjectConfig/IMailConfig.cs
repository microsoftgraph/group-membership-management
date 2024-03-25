// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Repositories.Contracts.InjectConfig
{
    public interface IMailConfig
    {
        public bool IsAdaptiveCardEnabled { get; }
        public bool GMMHasSendMailApplicationPermissions { get; set; }
        public string SenderAddress { get; set; }
    }
}
