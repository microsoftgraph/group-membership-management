# UI Setup

## Create the UI application and populate prereqs keyvault

The following PowerShell script will create a new application, `<solutionAbbreviation>`-ui-`<environmentAbbreviation>` and save these settings in the prereqs keyvault:

-   uiAppId
-   uiPasswordCredentialValue
-   uiTenantId

From your `PowerShell 7.x` command prompt navigate to the `Scripts\ApplicationSetupScripts\` folder of your `Public` repo and run these commands:

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
    * Go to the SignalR resource `<solutionAbbreviation>`-compute-`<environmentAbbreviation>`-signalr, it is located in your compute resource group, under the `Settings` -> `CORS` -> Add that URL as `Allowed Origins` -> Click `Save`.

### Run UI locally

Add the following variables to `.env`:

- REACT_APP_AAD_API_APP_CLIENT_ID (Set value as the application (client) id of `<solutionAbbreviation>`-webapi-`<environmentAbbreviation>`)
- REACT_APP_AAD_APP_SERVICE_BASE_URI (Set value as `https://<solutionAbbreviation>-compute-<environmentAbbreviation>-webapi.azurewebsites.net`)
- REACT_APP_AAD_APP_TENANT_ID (Set value as the azure tenant id where the UI/WebApi applications are installed)
- REACT_APP_AAD_UI_APP_CLIENT_ID (Set value as the application (client) id of `<solutionAbbreviation>`-ui-`<environmentAbbreviation>`)
- REACT_APP_APPINSIGHTS_CONNECTIONSTRING (Set value as the application insights connection string)
- REACT_APP_SHAREPOINTDOMAIN (Set value as the SharePoint domain for your tenant, i.e. m365x1234567.sharepoint.com )
- REACT_APP_DOMAINNAME (Set value as the domain name for your tenant, i.e. m365x1234567.onmicrosoft.com)
- REACT_APP_VERSION_NUMBER (Optional: app version value used by the UI)
- REACT_APP_MANAGE_MEMBERSHIP_FLAG (Optional: this value sets the state of the Manage Membership feature flag (true or false))
- REACT_APP_ENVIRONMENT_ABBREVIATION: (Set value with `<environmentAbbreviation>`)

### Optional: Setting up Playwright Integration tests
In order to run Playwright integration tests in your environment, you will need to do some manual steps to configure the account that will be used to run the tests.

1. Create a user in your tenant and grant them access to your UI by making sure they have the appropriate roles.
1. Set up authenticator 2FA for this user. Make sure you can access your UI page.
1. Create three secrets in your gmm-data-<env> keyvault
```
integrationTestDomain='<domain>' // i.e. 'domain.com'
integrationTestEmail='<user>@<domain>' // i.e. 'user@domain.com'
integrationTestPassword='<password>'
```
 You are now ready to run the Playwright Integration Tests! Make sure that you approve the manual validation for each run and that you have your Authenticator app ready to approve the sign-in request.

To run tests locally: 
   - Ensure your `.env` file contains the necessary environment variables, including the Playwright test secrets (domain, email, and password) in the following format:
   ```
   INTEGRATION_TEST_DOMAIN=<domain> // i.e. 'domain.com' or http://localhost:3000 *
   INTEGRATION_TEST_EMAIL='<user>@<domain>' // i.e. 'user@domain.com'
   INTEGRATION_TEST_PASSWORD='<password>'
   ```

Run the tests:
   - Use the following command to execute the Playwright tests:
     ```bash
     npx playwright test
     ```
   - This will run all the integration tests defined in your Playwright test suite.

* Note: If you want to run tests against local changes, make sure you set the domain to `http://localhost:3000`, and add it to the UI app registration as redirect URI. If you are running tests for the first time, you will need to run it against a remotely deployed UI in order to get a valid `storageState.json` file.

### Accessibility Testing
#### Installing Accessibility Insights for Web
1. Download the Extension:
   - Navigate to the [Accessibility Insights for Web](https://accessibilityinsights.io/downloads/) page.
   - Click on the "Download for Web" button.
   - Select "Add to Chrome" or "Add to Edge" depending on your browser.
   - Confirm by clicking "Add Extension" in the pop-up window.
2. Activate the Extension:
   - Once installed, click on the puzzle piece icon (extensions) in the top right corner of your browser.
   - Pin the Accessibility Insights extension for easy access.

#### Running Accessibility Insights
1. Open the Extension:
   - Click on the Accessibility Insights extension icon in your browser toolbar.
2. Run Automated Checks:
   - Select "FastPass" to run automated checks on your current webpage.
   - The tool will highlight any accessibility issues in red and provide guidance on how to address them.
3. Manual Testing:
   - For a more thorough assessment, use the "Assessment" feature.
   - Follow the step-by-step instructions to manually test various aspects of your UI for accessibility compliance.
4. Review and Fix Issues:
   - Review the issues identified by the tool.
   - Follow the provided guidance to fix the issues and ensure your UI changes do not introduce new accessibility bugs.

#### Regular Testing
- Run the Accessibility Insights checks whenever you make changes to the UI to catch any new issues early.
