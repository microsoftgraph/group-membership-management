// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
    public class MockMailRepository : IMailRepository
    {
        public Task SendMailAsync(EmailMessage emailMessage)
        {
            return Task.CompletedTask;
        }        
    }
}
