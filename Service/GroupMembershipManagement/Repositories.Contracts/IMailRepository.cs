// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IMailRepository
    {
        Task<HttpResponseMessage> SendMailAsync(EmailMessage emailMessage, Guid? runId);

        // Builds a styled HTML body for emailMessage. Embeds adaptiveCardJson when provided; returns null when no styled builder matches or styled fallbacks are disabled.
        Task<string?> BuildStyledFallbackEmailHtmlAsync(EmailMessage emailMessage, string? adaptiveCardJson = null);
    }
}