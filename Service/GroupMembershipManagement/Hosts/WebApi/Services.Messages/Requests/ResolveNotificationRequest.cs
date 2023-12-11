// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class ResolveNotificationRequest : RequestBase
    {
        public ResolveNotificationRequest(Guid id, string? userEmail, string resolution)
        {
            this.Id = id;
            this.UserEmail = userEmail ?? string.Empty;
            this.Resolution = resolution;
        }

        public Guid Id { get; }
        public string UserEmail { get; }
        public string Resolution { get; }
    }
}