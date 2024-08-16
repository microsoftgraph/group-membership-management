// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.SignalR;
using System.Diagnostics.CodeAnalysis;

namespace Services.WebApi
{
    [ExcludeFromCodeCoverage]
    public class SignalRService : Hub
    {
        public async Task NewMessage(long username, string message) =>
            await Clients.All.SendAsync("messageReceived", username, message);
    }
}
