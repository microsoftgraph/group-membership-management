// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts.InjectConfig;

namespace Services.Contracts
{
    public class NotifierConfig : INotifierConfig
    {
        public string WorkspaceId { get; set; }

        public NotifierConfig(
            string workspaceId
            )
        {
            WorkspaceId = workspaceId;
        }
    }
}
