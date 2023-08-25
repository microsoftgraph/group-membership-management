// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public interface IMessageTracker
    {
        Task AddAsync(string messageId);
        Task<string> GetNextMessageIdAsync();
        Task<int> GetMessageCountAsync();
        Task DeleteAsync();
    }
}
