## About The Project
[Groups Membership Management](https://microsoftit.visualstudio.com/OneITVSO/_git/STW-Sol-GrpMM-public)
The Groups Membership Management (GMM) UI serves as an interface for GMM admins and group owners to view and manage AAD and GMM groups that they own and that they belong to.
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
9. Add API permissions: click 'Add a permission', and select Microsoft Graph (under the Microsoft APIs tab). Then select 'Delegated Permissions', and search for User.Read and Directory.Read.All. Ensure both are checked, then click 'Add permissions' at the bottom of this panel.
10. Populate the {tenant id} and {client id} portions of ```UI\WebApp\wwwroot\appsettings.json``` with the values from your new app registration. This can be found in the "Overview" tab. If you are editing with a private repository, add a new folder in ```UI\WebApp\wwwroot\parameters``` with your environment abbreviation, and copy over the ```appsettings.json``` file. Populate the missing values.
11. Create a group in Azure AAD called "GMM Admins". Add relevant users as members of this group, then add this group's Object ID to the appsettings.json file.
12. If applicable, be sure to change these values in the private repository as well (this file will overwrite the public repo file!).