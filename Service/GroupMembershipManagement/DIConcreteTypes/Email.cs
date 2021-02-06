// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class Email : IEmail
    {
        public string SenderAddress { get; set; }
        public string SenderPassword { get; set; }
        public string SyncCompletedCCAddress { get; set; }
        public string SyncDisabledCCAddress { get; set; }
        public Email(string senderAddress, string senderPassword, string syncCompletedCCAddress, string syncDisabledCCAddress)
        {
            SenderAddress = senderAddress;
            SenderPassword = senderPassword;
            SyncCompletedCCAddress = syncCompletedCCAddress;
            SyncDisabledCCAddress = syncDisabledCCAddress;
        }

        public Email()
        {

        }
    }
}
