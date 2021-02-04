// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IMailRepository
    {
        Task SendMail(string subject, string content, string toEmailAddress, string ccEmailAddress, params string[] additionalContentParams);
    }
}