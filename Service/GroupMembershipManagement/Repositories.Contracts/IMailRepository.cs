// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts.InjectConfig;
using System;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IMailRepository
    {
        Task SendMailAsync(EmailMessage emailMessage, Guid? runId);
    }
}