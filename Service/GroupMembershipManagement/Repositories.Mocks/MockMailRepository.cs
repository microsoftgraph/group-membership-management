// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
    public class MockMailRepository : IMailRepository
    {
        public Task SendMail(string subject, string content, string toEmailAddress, params string[] additionalParams)
        {
            return Task.CompletedTask;
        }        
    }
}
