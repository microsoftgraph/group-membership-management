// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;
using Services.WebApi.Contracts;

namespace Services.Messages.Requests
{
    public class CopilotChatRequest : RequestBase
    {
        public CopilotChatRequest(string userMessage, List<CopilotChatMessage> conversationHistory, CopilotUserContext? userContext = null, string? conversationId = null, List<CopilotSourcePartResult>? workingQuery = null)
        {
            UserMessage = userMessage;
            ConversationHistory = conversationHistory ?? new List<CopilotChatMessage>();
            UserContext = userContext;
            ConversationId = conversationId;
            WorkingQuery = workingQuery;
        }

        public string UserMessage { get; private set; }

        public List<CopilotChatMessage> ConversationHistory { get; private set; }

        public CopilotUserContext? UserContext { get; private set; }

        public string? ConversationId { get; private set; }

        /// <summary>
        /// The full current working query (all inbound parts). May be an empty list for a brand-new query.
        /// </summary>
        public List<CopilotSourcePartResult>? WorkingQuery { get; private set; }
    }
}
