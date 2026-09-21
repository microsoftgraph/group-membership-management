// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using OpenAI.Chat;

namespace Services.WebApi.AI
{
    /// <summary>
    /// Creates the chat client used by <see cref="CopilotService"/>.
    /// </summary>
    public interface IChatClientFactory
    {
        /// <summary>
        /// Returns a configured chat client.
        /// </summary>
        ChatClient CreateChatClient();
    }
}
