// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System.Threading.Tasks;

namespace Hosts.TeamsChannelUpdater
{
    public interface IMessageEntity
    {
        Task SaveAsync(MembershipHttpRequest message);
        Task<MembershipHttpRequest> GetAsync();
        Task DeleteAsync();
    }
}
