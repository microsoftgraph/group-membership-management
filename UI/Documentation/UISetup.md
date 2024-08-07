# UI Setup

## Create the UI application and populate prereqs keyvault

The following PowerShell script will create a new application, `<solutionAbbreviation>`-ui-`<environmentAbbreviation>` and save these settings in the prereqs keyvault:

-   uiAppId
-   uiPasswordCredentialValue
-   uiTenantId

From your `PowerShell 7.x` command prompt navigate to the `UI\Scripts\` folder of your `Public` repo and run these commands:

    1.    . ./Set-UIAzureADApplication.ps1
    2.    Set-UIAzureADApplication	-SubscriptionName "<subscription-name>" `
                                        -SolutionAbbreviation "<solution-abbreviation>" `
                                        -EnvironmentAbbreviation "<environment-abbreviation>" `
                                        -DevTenantId "<dev-tenant-id>" `
                                        -TenantId "<keyvault-tenant-id>" `
                                        -Clean $false `
                                        -Verbose
Follow the instructions on the screen.

Notes:
- DevTenantId <app-tenant-id> - If the application is going to be installed in a different tenant, set that tenant id here.
- TenantId <keyvault-tenant-id> - This is the tenant where your GMM resources are located, i.e. keyvaults, storage account. If you only have one tenant, these will be set to the same tenant id.

> After running the script, ensure that the following api permissions are granted to the `<solutionAbbreviation>`-webapi-`<environmentAbbreviation>` application: User.Read, User.ReadBasic.All

## Update UI/Web API application settings

- Go to `<solutionAbbreviation>`-webapi-`<environmentAbbreviation>` application in Microsoft Entra ID -> `Expose an API` under `Manage` -> `Add a client application` -> provide the client id of `<solutionAbbreviation>`-ui-`<environmentAbbreviation>` -> Make sure that Authorized scopes is checked -> Click `Add application`

- Go to `<solutionAbbreviation>`-webapi-`<environmentAbbreviation>` application in Microsoft Entra ID -> `Manifest` under `Manage` -> Set `"accessTokenAcceptedVersion": 2`, -> Click `Save`

## Update Build/Release Pipeline variables

Add the following variables to your environment parameter file located in `/Service/GroupMembershipManagement/Hosts/UI/Infrastructure/compute/parameters/parameters.<environmentAbbreviation>.json`:

- **apiAppClientId** (Set value as the application (client) id of `<solutionAbbreviation>`-webapi-`<environmentAbbreviation>`)
- **apiServiceBaseUri** (Set value as `https://<solutionAbbreviation>-compute-<environmentAbbreviation>-webapi.azurewebsites.net`)
- **uiAppTenantId** (Set value as the azure tenant id where the UI/WebApi applications are installed)
- **uiAppClientId** (Set value as the application (client) id of `<solutionAbbreviation>`-ui-`<environmentAbbreviation>`)
- **sharepointDomain** (Set value as the SharePoint domain for your tenant, i.e. m365x1234567.sharepoint.com )
- **tenantDomain** (Set value as the domain name for your tenant, i.e. m365x1234567.onmicrosoft.com)

## Post-Deployment tasks

* Go to the static web app in your compute resource group. You should see a URL in the Overview page. Copy that URL.
    * Go to `<solutionAbbreviation>`-ui-`<environmentAbbreviation>` application in Microsoft Entra ID -> `Authentication` -> Add that URL as a Redirect URI -> Click `Save`
    * Go to `<solutionAbbreviation>`-compute-`<environmentAbbreviation>`-webapi in your compute resource group -> CORS under API -> Add that URL as `Allowed Origins` -> Click `Save`

### Run UI locally

Add the following variables to `.env`:

- REACT_APP_AAD_API_APP_CLIENT_ID (Set value as the application (client) id of `<solutionAbbreviation>`-webapi-`<environmentAbbreviation>`)
- REACT_APP_AAD_APP_SERVICE_BASE_URI (Set value as `https://<solutionAbbreviation>-compute-<environmentAbbreviation>-webapi.azurewebsites.net`)
- REACT_APP_AAD_APP_TENANT_ID (Set value as the azure tenant id where the UI/WebApi applications are installed)
- REACT_APP_AAD_UI_APP_CLIENT_ID (Set value as the application (client) id of `<solutionAbbreviation>`-ui-`<environmentAbbreviation>`)
- REACT_APP_APPINSIGHTS_CONNECTIONSTRING (Set value as the application insights connection string)
- REACT_APP_SHAREPOINTDOMAIN (Set value as the SharePoint domain for your tenant, i.e. m365x1234567.sharepoint.com )
- REACT_APP_DOMAINNAME (Set value as the domain name for your tenant, i.e. m365x1234567.onmicrosoft.com)
- REACT_APP_VERSION_NUMBER (Optional: this value is pulled from the build pipeline)
- REACT_APP_MANAGE_MEMBERSHIP_FLAG (Optional: this value sets the state of the Manage Membership feature flag (true or false))
- REACT_APP_ENVIRONMENT_ABBREVIATION: (Set value with `<environmentAbbreviation>`)
