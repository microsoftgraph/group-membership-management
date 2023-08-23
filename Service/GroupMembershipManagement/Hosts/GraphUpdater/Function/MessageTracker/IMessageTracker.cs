// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public interface IMessageTracker
    {
        Task Add(string messageId);
        Task<string> GetNextMessageId();
        Task Delete();
    }
}
