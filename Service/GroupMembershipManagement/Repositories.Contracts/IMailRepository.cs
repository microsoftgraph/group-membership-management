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
    }
}