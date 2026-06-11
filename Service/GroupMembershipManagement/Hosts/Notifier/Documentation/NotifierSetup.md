# Notifier Setup

The GMM Notifier uses adaptive cards to send Outlook Actionable Emails (OAM). OAM requires that a special id, called a `provider id`, be included in each adaptive card payload in order to validate the sender, content, and recipients of the message.

If an actionable message is sent from an email address that is not part of the approved senders list, the adaptive card will not be rendered.

These are the steps to procure a `provider id` for an environment.

## Create a Provider

1. Navigate to the [Outlook Actionable Messages Developer Dashboard].
1. Click the 'New Provider' button.
1. Fill out the required fields:
    Field Name | Description
    -|-
    Friendly Name | A name that represents the environment associated with the Originator Id. *Example: `<SolutionAbbreviation>` Notifier `<EnvironmentAbbreviation>`.*
    MsEntraAuth > MSEntra Application Id | The id of the OAM app. See [Create OAM Entra App](#create-oam-entra-app) below for setup instructions. Use the Provider Id in the actionable email dashboard as the input for the OamProviderId parameter in the script.
    MsEntraAuth > App Id Uri | This should be already set to 'api://auth-am-\<providerId\>/\<appId\>'
    MsEntraAuth > Supported Token Type | Should be set to AadToken
    Sender Email Address | The email address from which actionable messages will be sent.
    Target URLs | The endpoints where the actional messages will send responses. Example: https://`<SolutionAbbreviation>`-compute-`<EnvironmentAbbreviation>`-webapi.azurewebsites.net/.+
    Scope of Submission | This should be set to `Organization` scope.

After the submission is sent, an email will be sent to the Exchange admins asking them to review the request. The email contains an actionable message with a button that directs them to the [Outlook Actionable Messages Admin Dashboard] where they can approve the request.

## Create OAM Entra App

You can create the OAM Entra App using either the automated PowerShell script (recommended) or manually through the Azure Portal.

### Option 1: Automated Setup (Recommended)

Run the [Set-OAMEmailApplication.ps1](../../../../../Scripts/ApplicationSetupScripts/Set-OAMEmailApplication.ps1) script:

```powershell
Set-OAMEmailApplication -SolutionAbbreviation "<solution-abbreviation>" `
                        -EnvironmentAbbreviation "<environment-abbreviation>" `
                        -AppTenantId "<app-tenant-id>" `
                        -OamProviderId "<oam-provider-id>" `
                        -Clean $false
```

> **Note**: You will need the OAM Provider ID from the [Create a Provider](#create-a-provider) step before running this script. You can create the provider first, then run the script with the provider ID.

The script will output the Application (client) ID which you'll need for the `oamEntraAppId` parameter.

### Option 2: Manual Setup

For manual setup instructions, see [OAMEmail-Application-Creation-Instructions.md](../../../../../Scripts/ApplicationSetupScripts/Manual%20Setup%20Documentation/OAMEmail-Application-Creation-Instructions.md).




## Update parameters.env.json file with your new notifierId

1. Find your *parameters.`<EnvironmentAbbreviation>`.json* file in the [Infrastructure/data/parameters directory](../Infrastructure/data/parameters/)
1. Update the "notifierProviderId" parameter with the value of the Provider Id (Originator) from the provider that you created
1. Update the "oamEntraAppId" parameter with the Application (client) ID from the Entra app you created
1. Update the "oamEntraAppScope" parameter if needed (default is "Global")
1. Add and commit the change to the repository so that it gets written to the data keyvault

<!-- Link References -->
[Azure Portal]: https://portal.azure.com
[Outlook Actionable Messages Developer Dashboard]: https://aka.ms/publishoam
[Outlook Actionable Messages Admin Dashboard]: https://outlook.office.com/connectors/oam/Admin