// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;

namespace Services.Messages.Responses
{
    public class ResolveNotificationResponse : ResponseBase
    {
        public string CardJson { get; set; } = string.Empty;
    }
}
