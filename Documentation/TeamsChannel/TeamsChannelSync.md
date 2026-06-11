# TeamsChannel destination

TeamsChannelMembershipObtainer function is a membership provider which retrieves the owners of all the groups managed by GMM and synchronizes them with a specific Azure M365 group or security group which acts as the destination group. Different filters can be applied to retrieve the owners by job type.

## Code Setup for TeamsChannel

[Teams Channel Service Account Setup](../../Service/GroupMembershipManagement/Repositories.TeamsChannel/Documentation/TeamsChannelServiceAccountSetup.md)

## Apps with TeamsChannel functionality

The following apps are involved with TeamsChannel enablement:

1. UI / WebAPI: Allow for customers to create TeamsChannel syncs in the Sql table, but only supports shared channels.

2. JobTrigger: Added functionality in JobTrigger to grab the Team name accordingly from the correct Sql TeamsChannel table.

3. TeamsChannelMembershipObtainer: Functionality to grab the membership of a Teams channel and use that as the destination's membership. Currently only runs for non-standard (private and shared) channels.

4. TeamsChannelUpdater: Functionality to update a Teams channel and add / remove members from it to match the source query's membership.

## Setting up a TeamsChannel destination job

Before performing any syncs / onboarding to GMM, the group / channel owner **must do the following**:

1. Add the **teamsChannelServiceAccountUsername** user from the setup above as an **owner of the team / group** where the channel resides.

2. Add the **teamsChannelServiceAccountUsername** user from the setup above as an **owner of the channel**.

TeamsChannelMembershipObtainer function also uses a JSON object to define the query, like the one used by GroupMembershipObtainer function. The query specifies the rules that will dictate which jobs will be retrieved to subsequently determine the channel membership associated with each jobs' destination.

The TeamsChannelMembership query is defined based on two values, the Group Id and the Channel Id.

Let's look at an example:

```
[
  {
    "type": "TeamsChannelMembership",
    "value":
     {
        "objectId": "GROUP_ID_GUID",
        "channelId": "CHANNEL_ID_STRING"
    }
  }
]
```

Note: Currently, TeamsChannelMembership is only supported as a destination when creating through the UI, and not yet a source part, but can be added easily to the UI in the future.

Once you've adding the teams channel service account user as an owner of both the team and channel, you can **go to the GMM UI to create a TeamsChannel destination sync**!
