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
                                        -AppTenantId "<app-tenant-id>" `
                                        -KeyVaultTenantId "<keyvault-tenant-id>" `
                                        -Clean $false `
                                        -Verbose
Follow the instructions on the screen.

Note:
AppTenantId <app-tenant-id> - If the application is going to be installed in a different tenant, set that tenant id here.
KeyVaultTenantId <keyvault-tenant-id> - This is the tenant where your GMM resources are located, i.e. keyvaults, storage account.

If you only have one tenant, these will be set to the same tenant id.

## Update UI/Web API application settings

- Go to `<solutionAbbreviation>`-webapi-`<environmentAbbreviation>` application in Azure Active Directory -> `Expose an API` under `Manage` -> `Add a scope` -> fille the form:
* Scope name: user_impersonation
* Who can consent: Admins only
* Admin consent display name:user_impersonation
* Admin consent description:user_impersonation

- Go to `<solutionAbbreviation>`-webapi-`<environmentAbbreviation>` application in Azure Active Directory -> `Expose an API` under `Manage` -> `Add a client application` -> provide the client id of `<solutionAbbreviation>`-ui-`<environmentAbbreviation>` -> Make sure that Authorized scopes is checked -> Click `Add application`

- Go to `<solutionAbbreviation>`-webapi-`<environmentAbbreviation>` application in Azure Active Directory -> `Manifest` under `Manage` -> Set `"accessTokenAcceptedVersion": 2`, -> Click `Save`

## Update Build/Release Pipeline variables

Add the following variables to your build/release pipeline:

- REACT_APP_AAD_API_APP_CLIENT_ID_`<ENVIRONMENTABBREVIATION>` (Set value as the application (client) id of `<solutionAbbreviation>`-webapi-`<environmentAbbreviation>`)
- REACT_APP_AAD_APP_SERVICE_BASE_URI_`<ENVIRONMENTABBREVIATION>` (Set value as `https://<solutionAbbreviation>-compute-<environmentAbbreviation>-webapi.azurewebsites.net`)
- REACT_APP_AAD_APP_TENANT_ID_`<ENVIRONMENTABBREVIATION>` (Set value as the azure tenant id where the UI/WebApi applications are installed)
- REACT_APP_AAD_UI_APP_CLIENT_ID_`<ENVIRONMENTABBREVIATION>` (Set value as the application (client) id of `<solutionAbbreviation>`-ui-`<environmentAbbreviation>`)

### Deploy WebAPI & UI via Build/Release

### Run UI locally

Add the following variables to `.env`:

- REACT_APP_AAD_API_APP_CLIENT_ID_`<ENVIRONMENTABBREVIATION>` (Set value as the application (client) id of `<solutionAbbreviation>`-webapi-`<environmentAbbreviation>`)
- REACT_APP_AAD_APP_SERVICE_BASE_URI_`<ENVIRONMENTABBREVIATION>` (Set value as `https://<solutionAbbreviation>-compute-<environmentAbbreviation>-webapi.azurewebsites.net`)
- REACT_APP_AAD_APP_TENANT_ID_`<ENVIRONMENTABBREVIATION>` (Set value as the azure tenant id where the UI/WebApi applications are installed)
- REACT_APP_AAD_UI_APP_CLIENT_ID_`<ENVIRONMENTABBREVIATION>` (Set value as the application (client) id of `<solutionAbbreviation>`-ui-`<environmentAbbreviation>`)