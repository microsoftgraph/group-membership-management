// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class TeamsChannelConfig : ITeamsChannelConfig
    {
        public bool GMMHasTeamsChannelApplicationPermissions { get; set; }
        public string TeamsChannelServiceAccountUsername { get; set; }
        public string TeamsChannelServiceAccountPassword { get; set; }

        public TeamsChannelConfig(bool gmmHasTeamsChannelApplicationPermissions)
        {
            GMMHasTeamsChannelApplicationPermissions = gmmHasTeamsChannelApplicationPermissions;
        }

        public TeamsChannelConfig(bool gmmHasTeamsChannelApplicationPermissions, string teamsChannelServiceAccountUsername, string teamsChannelServiceAccountPassword)
        {
            GMMHasTeamsChannelApplicationPermissions = gmmHasTeamsChannelApplicationPermissions;
            TeamsChannelServiceAccountUsername = teamsChannelServiceAccountUsername;
            TeamsChannelServiceAccountPassword = teamsChannelServiceAccountPassword;
        }

        public TeamsChannelConfig()
        {
        }
    }
}
