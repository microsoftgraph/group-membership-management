// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models.Entities;
using System;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IMailRepository
    {
        Task SendMailAsync(EmailMessage emailMessage, Guid? runId);
    }
}