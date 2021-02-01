// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IMailRepository
    {
        Task SendMail(string subject, string content, string toEmailAddress, params string[] additionalParams);
    }
}