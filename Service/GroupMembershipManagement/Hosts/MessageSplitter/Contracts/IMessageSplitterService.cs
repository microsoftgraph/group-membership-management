// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;

namespace MessageSplitter.Contracts
{
    public interface IMessageSplitterService
    {
        public Task UpdateJobStatusAsync(Guid jobId, SyncStatus status);
    }
}
