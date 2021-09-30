// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Services.Entities
{
    public class MessageInformation
    {
        public byte[] Body { get; set; }
        public string SessionId { get; set; }
        public string LockToken { get; set; }
        public string MessageId { get; set; }
    }
}
