// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Repositories.Contracts.InjectConfig
{
    public interface ITeamsChannelConfig
    {
        public bool GMMHasTeamsChannelApplicationPermissions { get; set; }
        public Guid TeamsChannelServiceAccountObjectId { get; set; }
        public string TeamsChannelServiceAccountUsername { get; set; }
        public string TeamsChannelServiceAccountPassword { get; set; }
    }
}
