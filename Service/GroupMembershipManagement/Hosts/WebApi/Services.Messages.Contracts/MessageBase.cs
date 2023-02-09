// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Services.Messages.Contracts
{
    public abstract class MessageBase
    {
        public Guid InstanceId { get; private set; }

        public MessageBase()
        {
            InstanceId = Guid.NewGuid();
        }
    }
}
