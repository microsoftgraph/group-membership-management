// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts.InjectConfig;
using System;

namespace DIConcreteTypes
{
    public class TeamsChannelConfig : ITeamsChannelConfig
    {
        public bool GMMHasTeamsChannelApplicationPermissions { get; set; }
        public Guid TeamsChannelServiceAccountObjectId { get; set; }
        public string TeamsChannelServiceAccountUsername { get; set; }
        public string TeamsChannelServiceAccountPassword { get; set; }
        public string TeamsChannelAppRegistrationName { get; set; }

        public TeamsChannelConfig(bool gmmHasTeamsChannelApplicationPermissions)
        {
            GMMHasTeamsChannelApplicationPermissions = gmmHasTeamsChannelApplicationPermissions;
        }

        public TeamsChannelConfig(bool gmmHasTeamsChannelApplicationPermissions, string teamsChannelServiceAccountObjectId, string teamsChannelServiceAccountUsername, string teamsChannelServiceAccountPassword)
        {
            GMMHasTeamsChannelApplicationPermissions = gmmHasTeamsChannelApplicationPermissions;
            var validObjectId = Guid.TryParse(teamsChannelServiceAccountObjectId, out var objectId);
            TeamsChannelServiceAccountObjectId = validObjectId ? objectId : Guid.Empty;
            TeamsChannelServiceAccountUsername = teamsChannelServiceAccountUsername;
            TeamsChannelServiceAccountPassword = teamsChannelServiceAccountPassword;
        }

        public TeamsChannelConfig()
        {
        }
    }
}
