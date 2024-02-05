# Teams Channel Service Account Setup

1) Open the Azure Portal from the tenant where the Graph App is created in.
2) Ensure that the `Channel.ReadBasic.All` and `ChannelMember.ReadWrite.All` Delegated Permissions have been granted for the `<SolutionAbbreviation>-Graph-<EnvironmentAbbreviation>` application.
3) Enable 'Allow public client flows' in `<SolutionAbbreviation>-Graph-<EnvironmentAbbreviation>` application -> Authentication.
4) Create a new service account:

    * This can be done by creating a new user from the tenant where the Graph App is created in
    * Make sure the user has a usage location set
    * This account will be used by GMM to get information about channels and to add and remove users from channels. 
    * Please note username & password of this user.

5) Run [Set-TeamsChannelServiceAccountSecrets.ps1](/Scripts/Set-TeamsChannelServiceAccountSecrets.ps1) to store the service account information in the prereqs keyvault

    * Please make sure that a prereqs keyvault exists in your environment
    * Open the script in Windows PowerShell ISE
    * Add the following lines to the the end before running the script:
        ```
        $teamsChannelServiceAccountUsername = ConvertTo-SecureString -AsPlainText -Force "<Service Account Username>"
        $teamsChannelServiceAccountPassword = ConvertTo-SecureString -AsPlainText -Force "<Service Account Password>"
        $teamsChannelServiceAccountObjectId = ConvertTo-SecureString -AsPlainText -Force "<Service Account Object Id"

        Set-TeamsChannelServiceAccountSecrets   -SubscriptionName "<Subscription Name>" `
                                                -SolutionAbbreviation "<Solution Abbreviation>" `
                                                -EnvironmentAbbreviation "<Environment Abbreviation>" `
                                                -teamsChannelServiceAccountUsername $teamsChannelServiceAccountUsername `
                                                -teamsChannelServiceAccountPassword $teamsChannelServiceAccountPassword `
                                                -teamsChannelServiceAccountObjectId $teamsChannelServiceAccountObjectId
        ```
6) Assign the following two licenses to this user by going to [this](https://admin.microsoft.com/AdminPortal/Home#/licenses) link from your demo tenant page. You may have to unassign some licenses from other users to do this.

- Enterprise Mobility + Security E5
- Office 365 E5

  >Note: Make sure that the user you created has a usage location (you can verify this by going to User Profile). Otherwise, you will not be able to assign the above licenses to this user.

7) Ensure that this new service account is the owner of both the Team and the Teams Channel for each Teams Channel destination onboarded to GMM!