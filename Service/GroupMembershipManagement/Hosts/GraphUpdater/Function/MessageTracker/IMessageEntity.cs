// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public interface IMessageEntity
    {
        Task Save(MembershipHttpRequest message);
        Task<MembershipHttpRequest> Get();
        Task Delete();
    }
}
