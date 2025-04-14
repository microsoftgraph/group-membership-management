// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Repositories.Contracts.InjectConfig
{
    public interface ITeamsChannelConfig
    {
        public bool GMMHasTeamsChannelApplicationPermissions { get; set; }
        public string TeamsChannelServiceAccountUsername { get; set; }
        public string TeamsChannelServiceAccountPassword { get; set; }
    }
}
