// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface ITopicMessageSenderService
    {
        public Task SendMessageAsync(MembershipHttpRequest request);
    }
}