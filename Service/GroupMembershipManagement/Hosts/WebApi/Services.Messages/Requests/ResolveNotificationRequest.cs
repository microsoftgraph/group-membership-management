// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class ResolveNotificationRequest : RequestBase
    {
        public ResolveNotificationRequest(Guid id, string? userUPN, string resolution)
        {
            this.Id = id;
            this.UserUPN = userUPN ?? string.Empty;
            this.Resolution = resolution;
        }

        public Guid Id { get; }
        public string UserUPN { get; }
        public string Resolution { get; }
    }
}