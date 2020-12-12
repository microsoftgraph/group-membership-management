# Comparing Group Membership Management tool vs Azure Active Directory Dynamic Groups

## Azure Active Directory Dynamic Groups

"In Azure Active Directory (Azure AD), you can use rules to determine group membership based on user or device properties. Dynamic membership is supported for security groups or Microsoft 365 Groups. When a group membership rule is applied, user and device attributes are evaluated for matches with the membership rule. When an attribute changes for a user or device, all dynamic group rules in the organization are processed for membership changes. Users and devices are added or removed if they meet the conditions for a group. Security groups can be used for either devices or users, but Microsoft 365 Groups can be only user groups. Using Dynamic groups requires Azure AD premium P1 license."

## Group Membership Management

Group Membership Management tool can be used to synchronize group membership using Azure AD Security groups (one or multiple) as source and Azure AD Microsoft 365 groups as the destination.

## Group Membership Management vs Dynamic Groups
---

| Group Membership Management                                                                        | Dynamic Groups                                                         |     |
| -------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------- | --- |
| Requires additional setup (create Azure resources, repositories, deploy code)                      | No additional setup, it's built-in in Azure Active Directory           |
| Synchronizes Azure AD Security groups (one or more can be used as source) to a Microsoft 365 group | Not available                                                          |
| Group synchronization frequency can be specified per group                                         | Not available                                                          |
| Group synchronization can be scheduled with a start / end date time                                | Not available                                                          |
| Can be customized to add your own synchronization sources (programming required)                   | Not available                                                          |
| Disable individual synchronization jobs                                                            | Disable dynamic groups by converting it to static groups               |
| Not available                                                                                      | UI is provided to create simple rules                                  |
| Not available                                                                                      | Group synchronization runs when a user or device attribute is modified |
| Not available                                                                                      | Synchronizes users matching criteria to a Microsoft 365 group          |
| Not available                                                                                      | Synchronizes devices matching criteria to a Microsoft 365 group        |

For more information about GMM see [Group Membership Management](https://github.com/microsoftgraph/group-membership-management) on GitHub.

For more information about Azure AD Dynamic Groups see [Dynamic Groups](https://docs.microsoft.com/en-us/azure/active-directory/enterprise-users/groups-create-rule) documentation.
