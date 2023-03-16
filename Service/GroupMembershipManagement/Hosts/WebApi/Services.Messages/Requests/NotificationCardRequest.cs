// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class NotificationCardRequest : RequestBase
    {
        public Guid Id { get; set; } = Guid.Empty;
        public string UserUPN { get; set; } = string.Empty;

        public NotificationCardRequest()
        { }

        public NotificationCardRequest(Guid id, string userUpn)
        {
            Id = id;
            UserUPN = userUpn;
        }
    }
}