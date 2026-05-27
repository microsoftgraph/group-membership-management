// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Services.WebApi.Contracts
{
    public interface IOpenAIService
    {
        Task<string> GetTitleAsync(string prompt);
        Task<string> GetCompletionAsync(string systemPrompt, string userPrompt);
    }
}