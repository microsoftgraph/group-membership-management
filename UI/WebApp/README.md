## About The Project

[Groups Membership Management](https://microsoftit.visualstudio.com/OneITVSO/_git/STW-Sol-GrpMM-public)

The Groups Membership Management (GMM) UI serves as an interface for admins to view and manage groups that they own and that they belong to.

<br>

## Getting Started

To run your own instance of the GMM UI with working authentication features, you will need to complete the following configuration and setup steps: 

1. Create an app registration under your tenant. To do this, log in to your [Azure Portal](https://ms.portal.azure.com/#home) and select the "App Registrations" Azure Service.
2. Click the '+ New registration' button. This will bring you to a page that will allow you to register an application.
3. Create and enter name for your app.
4. Under "Supported Account Types", select "Accounts in this organizational directory only (Contoso only - Single tenant)".
5. The redirect URI can be configured later, so we will skip this step for now.
6. Click "Register".
7. Once you have the base url for your application, you will need to enter the redirect URI for your app. This can be done in the "Authentication" tab under the "Manage" heading for your app (still under App registrations from earlier).
8. Select "Add a platform" if "Single-page application" does not yet exist, and then add the following redirect URIs: ```https://{base uri}/login-callback``` and ```https://{base uri}/logout-callback```.
9. Populate the {tenant id} and {client id} portions of ```UI\WebApp\wwwroot\appsettings.json``` with the values from your new app registration. This can be found in the "Overview" tab.