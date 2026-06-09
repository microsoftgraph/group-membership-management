// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;
using Services.WebApi.Contracts;

namespace Services.Messages.Requests
{
    public class CopilotChatRequest : RequestBase
    {
        public CopilotChatRequest(string userMessage, List<CopilotChatMessage> conversationHistory, CopilotUserContext? userContext = null, string? currentFilter = null)
        {
            UserMessage = userMessage;
            ConversationHistory = conversationHistory ?? new List<CopilotChatMessage>();
            UserContext = userContext;
            CurrentFilter = currentFilter;
        }

        public string UserMessage { get; private set; }

        public List<CopilotChatMessage> ConversationHistory { get; private set; }

        public CopilotUserContext? UserContext { get; private set; }

        public string? CurrentFilter { get; private set; }
    }
}
